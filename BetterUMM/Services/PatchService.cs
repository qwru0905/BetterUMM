using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using BetterUMM.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterUMM.Services
{
    public enum PatchStatus { NotInstalled, AssemblyInjection, Doorstop }

    public class PatchService
    {
        private const string StarterTypeName = "UnityModManagerStarter";
        private const string StarterNamespace = "Injection";
        private const string UmmSubDir = "UnityModManager";
        private const string UmmDllName = "UnityModManager.dll";
        private const string DoorstopConfigFile = "doorstop_config.ini";
        private const string DoorstopDllFile = "winhttp.dll";

        // ── 상태 확인 ────────────────────────────────────────────────────

        public PatchStatus GetPatchStatus(GameInfo game)
        {
            string gameRoot = Path.GetDirectoryName(game.Path)!;

            if (File.Exists(Path.Combine(gameRoot, DoorstopDllFile)) &&
                File.Exists(Path.Combine(gameRoot, DoorstopConfigFile)))
                return PatchStatus.Doorstop;

            string assemblyPath = Path.Combine(game.GameDataPath, "Managed", game.AssemblyName);
            if (File.Exists(assemblyPath))
            {
                using var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
                if (asm.Modules.Any(m => m.Types.Any(t => t.Name == StarterTypeName)))
                    return PatchStatus.AssemblyInjection;
            }

            return PatchStatus.NotInstalled;
        }

        // ── Doorstop 방식 (Steam 무결성 검사 통과) ────────────────────────
        // winhttp.dll 프록시가 게임 시작 시 target_assembly를 먼저 로드함.
        // 기존 게임 파일을 수정하지 않으므로 Steam 무결성 검사에서 살아남음.

        public bool InstallDoorstop(GameInfo game, string doorstopX64Path, string doorstopX86Path, string[] libraryPaths)
        {
            string gameRoot = Path.GetDirectoryName(game.Path)!;
            string managedPath = Path.Combine(game.GameDataPath, "Managed");
            string ummDir = Path.Combine(managedPath, UmmSubDir);
            string doorstopPath = Path.Combine(gameRoot, DoorstopDllFile);
            string configPath = Path.Combine(gameRoot, DoorstopConfigFile);
            string gameConfigPath = Path.Combine(ummDir, "Config.xml");

            var backups = new List<string>();
            try
            {
                if (!Directory.Exists(ummDir))
                    Directory.CreateDirectory(ummDir);

                bool? is64 = IsExecutable64Bit(game.Path);
                string srcDll = is64 == true ? doorstopX64Path : doorstopX86Path;

                MakeBackup(doorstopPath, backups);
                MakeBackup(configPath, backups);
                MakeBackup(gameConfigPath, backups);

                File.Copy(srcDll, doorstopPath, true);

                // doorstop_config.ini 경로는 게임 실행파일 기준 상대경로
                string dataFolderName = Path.GetFileName(game.GameDataPath);
                string relTarget = Path.Combine(dataFolderName, "Managed", UmmSubDir, UmmDllName);
                File.WriteAllText(configPath,
                    $"[General]{Environment.NewLine}enabled = true{Environment.NewLine}target_assembly = {relTarget}");

                foreach (var lib in libraryPaths)
                {
                    string dest = Path.Combine(ummDir, Path.GetFileName(lib));
                    File.Copy(lib, dest, true);
                }

                ExportConfig(game, gameConfigPath);

                DeleteBackups(backups);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "InstallDoorstop");
                RestoreBackups(backups);
                return false;
            }
        }

        private void ExportConfig(GameInfo game, string destPath)
        {
            var serializer = new XmlSerializer(typeof(GameInfo));
            using var writer = new StreamWriter(destPath);
            serializer.Serialize(writer, game);
        }

        public bool RemoveDoorstop(GameInfo game)
        {
            string gameRoot = Path.GetDirectoryName(game.Path)!;
            string ummDir = Path.Combine(game.GameDataPath, "Managed", UmmSubDir);

            try
            {
                TryDelete(Path.Combine(gameRoot, DoorstopDllFile));
                TryDelete(Path.Combine(gameRoot, DoorstopConfigFile));
                if (Directory.Exists(ummDir))
                    Directory.Delete(ummDir, true);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "RemoveDoorstop");
                return false;
            }
        }

        // ── Assembly 주입 방식 ────────────────────────────────────────────
        // UnityModManagerStarter 타입을 게임 어셈블리에 직접 embed하고
        // entry point 메서드에 Call Start() 인스트럭션을 삽입함.

        public bool InstallAssembly(GameInfo game, string[] libraryPaths)
        {
            if (!TryParseEntryPoint(game, out var typeName, out var methodName, out var place))
            {
                LoggerService.Log($"Entry point not found in {game.Name}", LogLevel.Error);
                return false;
            }

            string assemblyFileName = ExtractAssemblyFileName(game.PatchTarget, game.AssemblyName);
            string managedPath = Path.Combine(game.GameDataPath, "Managed");
            string assemblyPath = Path.Combine(managedPath, assemblyFileName);
            string originalPath = assemblyPath + ".original_";
            string ummDir = Path.Combine(managedPath, UmmSubDir);

            var backups = new List<string>();
            try
            {
                Directory.CreateDirectory(ummDir);
                MakeBackup(assemblyPath, backups);

                if (!File.Exists(originalPath))
                    File.Copy(assemblyPath, originalPath, false);

                using var assembly = AssemblyDefinition.ReadAssembly(
                    assemblyPath, new ReaderParameters { ReadWrite = true });

                RemoveInjectedStarter(assembly);

                var entryMethod = FindMethod(assembly, typeName, methodName);
                if (entryMethod == null)
                {
                    LoggerService.Log($"Entry point not found: {typeName}.{methodName}", LogLevel.Error);
                    return false;
                }

                var starter = BuildStarterType(assembly.MainModule);
                assembly.MainModule.Types.Add(starter);

                var startRef = assembly.MainModule.ImportReference(
                    starter.Methods.First(m => m.Name == "Start"));
                var callInstr = Instruction.Create(OpCodes.Call, startRef);
                var il = entryMethod.Body.GetILProcessor();

                if (place == "before")
                    il.InsertBefore(entryMethod.Body.Instructions[0], callInstr);
                else
                {
                    var ret = entryMethod.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret);
                    if (ret != null) il.InsertBefore(ret, callInstr);
                    else il.Append(callInstr);
                }

                assembly.Write();

                foreach (var lib in libraryPaths)
                    File.Copy(lib, Path.Combine(ummDir, Path.GetFileName(lib)), true);

                DeleteBackups(backups);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "InstallAssembly");
                RestoreBackups(backups);
                return false;
            }
        }

        public bool RemoveAssembly(GameInfo game)
        {
            if (!TryParseEntryPoint(game, out var typeName, out var methodName, out _))
                return false;

            string assemblyFileName = ExtractAssemblyFileName(game.PatchTarget, game.AssemblyName);
            string managedPath = Path.Combine(game.GameDataPath, "Managed");
            string assemblyPath = Path.Combine(managedPath, assemblyFileName);
            string originalPath = assemblyPath + ".original_";
            string ummDir = Path.Combine(managedPath, UmmSubDir);

            try
            {
                // .original_ 백업이 있으면 원본 복원 (원본 UMM 방식)
                if (File.Exists(originalPath))
                {
                    File.Copy(originalPath, assemblyPath, overwrite: true);
                    File.Delete(originalPath);
                    LoggerService.Log($"Restored original assembly from {originalPath}");
                }
                else
                {
                    // 백업 없으면 직접 어셈블리에서 Starter 제거
                    using var assembly = AssemblyDefinition.ReadAssembly(
                        assemblyPath, new ReaderParameters { ReadWrite = true });

                    var injected = assembly.MainModule.Types.FirstOrDefault(t => t.Name == StarterTypeName);
                    if (injected == null) return true;

                    var entryMethod = FindMethod(assembly, typeName, methodName);
                    if (entryMethod != null)
                    {
                        var il = entryMethod.Body.GetILProcessor();
                        var callToRemove = entryMethod.Body.Instructions.FirstOrDefault(i =>
                            i.OpCode == OpCodes.Call &&
                            i.Operand is MethodReference mr &&
                            mr.Name == "Start" &&
                            mr.DeclaringType.Name == StarterTypeName);
                        if (callToRemove != null)
                            il.Remove(callToRemove);
                    }

                    assembly.MainModule.Types.Remove(injected);
                    assembly.Write();
                }

                if (Directory.Exists(ummDir))
                    Directory.Delete(ummDir, true);

                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "RemoveAssembly");
                return false;
            }
        }

        private string ExtractAssemblyFileName(string patchTarget, string defaultAssembly)
        {
            if (string.IsNullOrEmpty(patchTarget)) return defaultAssembly;
            var openBracket = patchTarget.IndexOf('[');
            var closeBracket = patchTarget.IndexOf(']');
            if (openBracket >= 0 && closeBracket > openBracket)
            {
                return patchTarget.Substring(openBracket + 1, closeBracket - openBracket - 1);
            }
            return defaultAssembly;
        }

        // ── UnityModManagerStarter 타입 IL 빌드 ──────────────────────────
        // 이 타입이 게임 어셈블리에 embed되어 게임 시작 시 UMM DLL을 로드함.
        // 생성되는 C# 동치:
        //
        //   namespace Injection {
        //     public static class UnityModManagerStarter {
        //       public static void Start() {
        //         string loc = Assembly.GetExecutingAssembly().Location;
        //         string dir = Path.GetDirectoryName(loc);
        //         string dll = Path.Combine(dir, "UnityModManager", "UnityModManager.dll");
        //         if (!File.Exists(dll)) return;
        //         Assembly asm = Assembly.LoadFile(dll);
        //         Type t = asm.GetType("UnityModManagerNet.Injector");
        //         if (t == null) return;
        //         MethodInfo run = t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        //         if (run == null) return;
        //         run.Invoke(null, new object[] { false });
        //       }
        //     }
        //   }

        private TypeDefinition BuildStarterType(ModuleDefinition module)
        {
            var type = new TypeDefinition(
                StarterNamespace, StarterTypeName,
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract |
                Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);

            var method = new MethodDefinition("Start",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static |
                Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            method.Body.InitLocals = true;

            // locals: 0=location, 1=dir, 2=dllPath, 3=asm, 4=type, 5=methodInfo
            var strType = module.TypeSystem.String;
            var asmType = module.ImportReference(typeof(Assembly));
            var typeType = module.ImportReference(typeof(Type));
            var miType = module.ImportReference(typeof(MethodInfo));
            method.Body.Variables.Add(new VariableDefinition(strType));
            method.Body.Variables.Add(new VariableDefinition(strType));
            method.Body.Variables.Add(new VariableDefinition(strType));
            method.Body.Variables.Add(new VariableDefinition(asmType));
            method.Body.Variables.Add(new VariableDefinition(typeType));
            method.Body.Variables.Add(new VariableDefinition(miType));

            // 메서드 참조
            var refGetExecAsm = module.ImportReference(
                typeof(Assembly).GetMethod("GetExecutingAssembly"));
            var refGetLocation = module.ImportReference(
                typeof(Assembly).GetProperty("Location")!.GetGetMethod()!);
            var refGetDirName = module.ImportReference(
                typeof(Path).GetMethod("GetDirectoryName", new[] { typeof(string) }));
            var refCombine3 = module.ImportReference(
                typeof(Path).GetMethod("Combine", new[] { typeof(string), typeof(string), typeof(string) }));
            var refFileExists = module.ImportReference(
                typeof(File).GetMethod("Exists", new[] { typeof(string) }));
            var refLoadFile = module.ImportReference(
                typeof(Assembly).GetMethod("LoadFile", new[] { typeof(string) }));
            var refAsmGetType = module.ImportReference(
                typeof(Assembly).GetMethod("GetType", new[] { typeof(string) }));
            var refTypeGetMethod = module.ImportReference(
                typeof(Type).GetMethod("GetMethod", new[] { typeof(string), typeof(BindingFlags) }));
            var refInvoke = module.ImportReference(
                typeof(MethodInfo).GetMethod("Invoke", new[] { typeof(object), typeof(object[]) }));
            var refBoolType = module.ImportReference(typeof(bool));
            var refObjType = module.ImportReference(typeof(object));

            var il = method.Body.GetILProcessor();
            var ret = il.Create(OpCodes.Ret);

            var vars = method.Body.Variables;

            // loc = Assembly.GetExecutingAssembly().Location
            il.Emit(OpCodes.Call, refGetExecAsm);
            il.Emit(OpCodes.Callvirt, refGetLocation);
            il.Emit(OpCodes.Stloc, vars[0]);

            // dir = Path.GetDirectoryName(loc)
            il.Emit(OpCodes.Ldloc, vars[0]);
            il.Emit(OpCodes.Call, refGetDirName);
            il.Emit(OpCodes.Stloc, vars[1]);

            // dllPath = Path.Combine(dir, "UnityModManager", "UnityModManager.dll")
            il.Emit(OpCodes.Ldloc, vars[1]);
            il.Emit(OpCodes.Ldstr, "UnityModManager");
            il.Emit(OpCodes.Ldstr, "UnityModManager.dll");
            il.Emit(OpCodes.Call, refCombine3);
            il.Emit(OpCodes.Stloc, vars[2]);

            // if (!File.Exists(dllPath)) return
            il.Emit(OpCodes.Ldloc, vars[2]);
            il.Emit(OpCodes.Call, refFileExists);
            il.Emit(OpCodes.Brfalse, ret);

            // asm = Assembly.LoadFile(dllPath)
            il.Emit(OpCodes.Ldloc, vars[2]);
            il.Emit(OpCodes.Call, refLoadFile);
            il.Emit(OpCodes.Stloc, vars[3]);

            // type = asm.GetType("UnityModManagerNet.Injector")
            il.Emit(OpCodes.Ldloc, vars[3]);
            il.Emit(OpCodes.Ldstr, "UnityModManagerNet.Injector");
            il.Emit(OpCodes.Callvirt, refAsmGetType);
            il.Emit(OpCodes.Stloc, vars[4]);

            // if (type == null) return
            il.Emit(OpCodes.Ldloc, vars[4]);
            il.Emit(OpCodes.Brfalse, ret);

            // run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
            il.Emit(OpCodes.Ldloc, vars[4]);
            il.Emit(OpCodes.Ldstr, "Run");
            il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Static));
            il.Emit(OpCodes.Callvirt, refTypeGetMethod);
            il.Emit(OpCodes.Stloc, vars[5]);

            // if (run == null) return
            il.Emit(OpCodes.Ldloc, vars[5]);
            il.Emit(OpCodes.Brfalse, ret);

            // run.Invoke(null, new object[] { false })
            il.Emit(OpCodes.Ldloc, vars[5]);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, refObjType);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);  // false
            il.Emit(OpCodes.Box, refBoolType);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, refInvoke);
            il.Emit(OpCodes.Pop);

            il.Append(ret);

            type.Methods.Add(method);
            return type;
        }

        // ── 내부 유틸리티 ─────────────────────────────────────────────────

        // PatchTarget 형식: "Namespace.Class.Method:Before/After"
        // 예) "App.Awake:After", "TH20.MainScript.Start:Before"
        private bool TryParseEntryPoint(GameInfo game, out string typeName, out string methodName, out string place)
        {
            typeName = methodName = place = string.Empty;
            if (string.IsNullOrEmpty(game.PatchTarget))
            {
                LoggerService.Log($"TryParseEntryPoint: PatchTarget is null or empty for {game.Name}", LogLevel.Error);
                return false;
            }

            LoggerService.Log($"TryParseEntryPoint: Parsing PatchTarget for {game.Name}: {game.PatchTarget}");

            string target = game.PatchTarget;
            // assembly name in brackets: [Assembly.dll]Namespace.Class.Method:Before
            var bracketIdx = target.LastIndexOf(']');
            if (bracketIdx >= 0)
            {
                target = target[(bracketIdx + 1)..];
                LoggerService.Log($"TryParseEntryPoint: Assembly part stripped, remaining: {target}");
            }

            var colonIdx = target.LastIndexOf(':');
            string fullMethod;

            if (colonIdx >= 0)
            {
                place = target[(colonIdx + 1)..].ToLower();
                fullMethod = target[..colonIdx];
            }
            else
            {
                place = "after";
                fullMethod = target;
            }

            var lastDot = fullMethod.LastIndexOf('.');
            if (lastDot < 0)
            {
                LoggerService.Log($"TryParseEntryPoint: Failed to find last dot in {fullMethod}", LogLevel.Error);
                return false;
            }

            typeName = fullMethod[..lastDot];
            methodName = fullMethod[(lastDot + 1)..];

            LoggerService.Log($"TryParseEntryPoint: Success - Type: {typeName}, Method: {methodName}, Place: {place}");
            return true;
        }

        private MethodDefinition? FindMethod(AssemblyDefinition assembly, string typeName, string methodName)
        {
            foreach (var module in assembly.Modules)
            {
                var type = module.Types.FirstOrDefault(t => t.FullName == typeName)
                    ?? module.Types.FirstOrDefault(t => t.Name == typeName)
                    ?? module.Types.SelectMany(t => t.NestedTypes)
                              .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

                if (type != null)
                    return type.Methods.FirstOrDefault(m => m.Name == methodName);
            }
            return null;
        }

        private void RemoveInjectedStarter(AssemblyDefinition assembly)
        {
            foreach (var module in assembly.Modules)
            {
                var t = module.Types.FirstOrDefault(x => x.Name == StarterTypeName);
                if (t != null) module.Types.Remove(t);
            }
        }

        // PE 헤더에서 64-bit 여부 판별
        private static bool? IsExecutable64Bit(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt16() != 0x5A4D) return null;        // MZ
                stream.Seek(60, SeekOrigin.Begin);
                stream.Seek(reader.ReadInt32(), SeekOrigin.Begin);      // e_lfanew
                if (reader.ReadUInt32() != 0x00004550) return null;     // PE\0\0
                var machine = reader.ReadUInt16();
                return machine == 0x8664 || machine == 0x0200;          // AMD64 / IA64
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"IsExecutable64Bit: {filePath}");
                return null;
            }
        }

        private static void MakeBackup(string path, List<string> tracked)
        {
            if (!File.Exists(path)) return;
            File.Copy(path, path + ".bak", true);
            tracked.Add(path);
        }

        private static void RestoreBackups(List<string> tracked)
        {
            foreach (var path in tracked)
                if (File.Exists(path + ".bak"))
                    File.Move(path + ".bak", path, true);
        }

        private static void DeleteBackups(List<string> tracked)
        {
            foreach (var path in tracked)
                TryDelete(path + ".bak");
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}