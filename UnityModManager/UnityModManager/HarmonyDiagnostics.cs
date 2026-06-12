using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace UnityModManagerNet
{
    public partial class UnityModManager
    {
        /// <summary>
        /// Resolves which mods patched a given method via Harmony, for diagnostic logging.
        /// All public entry points swallow exceptions so diagnostics never break normal logging.
        /// </summary>
        internal static class HarmonyDiagnostics
        {
            // Matches Harmony/MonoMod-generated dynamic method names like
            // "DMD<Namespace.Type::MethodName>_a1b2c3d4".
            private static readonly Regex DmdNameRegex =
                new Regex(@"^DMD<(?<type>.+)::(?<method>[^>]+)>_[0-9A-Fa-f]+$", RegexOptions.Compiled);

            // Matches the leading "Namespace.Type.Method (" of a Unity managed stack trace line.
            private static readonly Regex StackTraceLineRegex =
                new Regex(@"^(?<full>[^\s(]+)\s*\(", RegexOptions.Compiled);

            /// <summary>
            /// Called from Application.logMessageReceived. Only LogType.Exception is handled.
            /// </summary>
            internal static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Exception)
                    return;

                try
                {
                    var method = TryFindMethodFromStackTrace(stackTrace);
                    if (method == null)
                        return;

                    var info = DescribePatches(method);
                    if (!string.IsNullOrEmpty(info))
                        Logger.Log($"  ↳ {info}");
                }
                catch
                {
                    // Diagnostics must never break the original log output.
                }
            }

            /// <summary>
            /// Returns a human-readable description of Harmony patches related to <paramref name="method"/>,
            /// or an empty string if none are found or an error occurs.
            /// </summary>
            internal static string DescribePatches(MethodBase method)
            {
                if (method == null)
                    return string.Empty;

                try
                {
                    // Case A: method is itself a patched target.
                    var patches = Harmony.GetPatchInfo(method);
                    if (patches != null)
                    {
                        var description = FormatPatches(method, patches);
                        if (!string.IsNullOrEmpty(description))
                            return description;
                    }

                    // Case B: method is a Harmony/MonoMod-generated dynamic method ("DMD<Type::Method>_hash").
                    var match = DmdNameRegex.Match(method.Name);
                    if (match.Success)
                    {
                        var original = FindMethod(match.Groups["type"].Value, match.Groups["method"].Value);
                        if (original != null)
                        {
                            var originalPatches = Harmony.GetPatchInfo(original);
                            if (originalPatches != null)
                            {
                                var description = FormatPatches(original, originalPatches);
                                if (!string.IsNullOrEmpty(description))
                                    return description;
                            }
                        }
                    }

                    // Case C: method is itself a mod's Prefix/Postfix/Transpiler/Finalizer delegate.
                    foreach (var original in Harmony.GetAllPatchedMethods())
                    {
                        var originalPatches = Harmony.GetPatchInfo(original);
                        if (originalPatches == null)
                            continue;

                        var owner = FindOwnerOfPatchMethod(originalPatches, method, out var label);
                        if (owner != null)
                        {
                            return $"This method is a {label} patch registered by {ResolveModName(owner)}, targeting {DescribeMethod(original)}";
                        }
                    }
                }
                catch
                {
                    return string.Empty;
                }

                return string.Empty;
            }

            private static string FormatPatches(MethodBase method, Patches patches)
            {
                var groups = new List<string>();

                AddGroup(groups, "Prefix", patches.Prefixes);
                AddGroup(groups, "Postfix", patches.Postfixes);
                AddGroup(groups, "Transpiler", patches.Transpilers);
                AddGroup(groups, "Finalizer", patches.Finalizers);

                if (groups.Count == 0)
                    return string.Empty;

                return $"Harmony patches on {DescribeMethod(method)}: {string.Join(", ", groups)}";
            }

            private static void AddGroup(List<string> groups, string label, IReadOnlyCollection<Patch> patchList)
            {
                if (patchList == null || patchList.Count == 0)
                    return;

                foreach (var owner in patchList.Select(p => p.owner).Distinct())
                {
                    groups.Add($"{ResolveModName(owner)} ({label})");
                }
            }

            private static string FindOwnerOfPatchMethod(Patches patches, MethodBase method, out string label)
            {
                foreach (var patch in patches.Prefixes)
                {
                    if (patch.PatchMethod == method)
                    {
                        label = "Prefix";
                        return patch.owner;
                    }
                }
                foreach (var patch in patches.Postfixes)
                {
                    if (patch.PatchMethod == method)
                    {
                        label = "Postfix";
                        return patch.owner;
                    }
                }
                foreach (var patch in patches.Transpilers)
                {
                    if (patch.PatchMethod == method)
                    {
                        label = "Transpiler";
                        return patch.owner;
                    }
                }
                foreach (var patch in patches.Finalizers)
                {
                    if (patch.PatchMethod == method)
                    {
                        label = "Finalizer";
                        return patch.owner;
                    }
                }

                label = null;
                return null;
            }

            private static string DescribeMethod(MethodBase method)
            {
                return $"{method.DeclaringType?.FullName}.{method.Name}";
            }

            private static string ResolveModName(string ownerId)
            {
                var mod = modEntries.FirstOrDefault(m => m.Info.Id == ownerId);
                return mod != null ? mod.Info.DisplayName : ownerId;
            }

            private static MethodBase TryFindMethodFromStackTrace(string stackTrace)
            {
                if (string.IsNullOrEmpty(stackTrace))
                    return null;

                var firstLine = stackTrace.Split('\n').FirstOrDefault();
                if (string.IsNullOrEmpty(firstLine))
                    return null;

                var match = StackTraceLineRegex.Match(firstLine.Trim());
                if (!match.Success)
                    return null;

                var full = match.Groups["full"].Value;
                var lastDot = full.LastIndexOf('.');
                if (lastDot < 0 || lastDot == full.Length - 1)
                    return null;

                var typeName = full.Substring(0, lastDot);
                var methodName = full.Substring(lastDot + 1);

                return FindMethod(typeName, methodName);
            }

            private static MethodBase FindMethod(string typeFullName, string methodName)
            {
                var type = FindType(typeFullName);
                if (type == null)
                    return null;

                return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == methodName);
            }

            private static Type FindType(string fullName)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type;
                    try
                    {
                        type = assembly.GetType(fullName);
                    }
                    catch
                    {
                        continue;
                    }

                    if (type != null)
                        return type;
                }

                return null;
            }
        }
    }
}
