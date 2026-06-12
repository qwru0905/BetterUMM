# Harmony 패치 에러 출처 표시 기능 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UMM에서 Harmony로 패치된 메소드에서 예외가 발생했을 때, 어떤 모드가 해당 메소드에 Prefix/Postfix/Transpiler/Finalizer 패치를 걸었는지를 로그에 자동으로 함께 출력한다.

**Architecture:** `UnityModManager/UnityModManager/`에 새 파일 `HarmonyDiagnostics.cs`를 추가해 `MethodBase`로부터 Harmony 패치 정보를 사람이 읽을 수 있는 문자열로 변환하는 `DescribePatches`를 구현한다. 이 함수를 (1) `ModManager.cs`의 `Logger.LogException`에서 예외의 첫 스택 프레임에 대해 호출하고, (2) `Application.logMessageReceived`(`LogType.Exception`)에서 스택 트레이스 첫 줄을 파싱한 메소드에 대해 호출한다. 모든 진단 로직은 예외를 삼켜 원래 로그 출력에 영향을 주지 않는다.

**Tech Stack:** C# (netstandard2.1), Lib.Harmony 2.3.6 (`HarmonyLib`), UnityEngine (`Application.logMessageReceived`, `LogType`), System.Reflection, System.Text.RegularExpressions.

**참고 설계 문서:** `docs/superpowers/specs/2026-06-13-harmony-error-tracing-design.md`

**빌드 검증 명령 (모든 Task에서 공통 사용):**
```bash
dotnet build UnityModManager/UnityModManager/UnityModManager.csproj -c Release -p:SolutionDir="D:/01_Code/10_CSharp/01_BetterUMM/"
```
기대 결과: `Build succeeded.` 및 `0 Error(s)`. (경고는 기존에도 다수 존재하므로 무시 가능)

이 빌드는 post-build target에 의해 `BetterUMM/Resources/UnityModManager/UnityModManager.dll`을 자동 갱신한다.

---

### Task 1: `HarmonyDiagnostics` 핵심 로직 작성 (`DescribePatches`)

**Files:**
- Create: `UnityModManager/UnityModManager/HarmonyDiagnostics.cs`

- [ ] **Step 1: `HarmonyDiagnostics.cs` 작성**

다음 내용으로 새 파일을 작성한다.

```csharp
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
```

- [ ] **Step 2: 빌드해서 컴파일 오류가 없는지 확인**

Run:
```bash
dotnet build UnityModManager/UnityModManager/UnityModManager.csproj -c Release -p:SolutionDir="D:/01_Code/10_CSharp/01_BetterUMM/"
```
Expected: `Build succeeded.`, `0 Error(s)`. (이 시점에서는 `HarmonyDiagnostics`가 아직 어디서도 호출되지 않으므로 "사용되지 않음" 경고가 새로 추가될 수 있음 - 정상)

- [ ] **Step 3: Commit**

```bash
git add UnityModManager/UnityModManager/HarmonyDiagnostics.cs
git commit -m "feat: Harmony 패치 정보를 메소드로부터 조회하는 HarmonyDiagnostics 추가"
```

---

### Task 2: `Logger.LogException`에서 패치 정보 출력

**Files:**
- Modify: `UnityModManager/UnityModManager/ModManager.cs:114-137` (`Logger.LogException` 메소드)

- [ ] **Step 1: `using` 추가**

`UnityModManager/UnityModManager/ModManager.cs` 최상단의 using 목록은 다음과 같다:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using UnityEngine;
using dnlib.DotNet;
```

여기에 `System.Diagnostics`를 추가한다 (StackTrace 사용을 위함). `System.Reflection`은 이미 존재한다.

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using UnityEngine;
using dnlib.DotNet;
```

- [ ] **Step 2: `LogException` 메소드 수정**

현재 `ModManager.cs`의 `LogException`은 다음과 같다 (라인 130-137 부근):

```csharp
            /// <summary>
            /// [0.17.0]
            /// </summary>
            public static void LogException(string key, Exception e, string prefix)
            {
                if (string.IsNullOrEmpty(key))
                    Write($"{prefix}{e.GetType().Name} - {e.Message}");
                else
                    Write($"{prefix}{key}: {e.GetType().Name} - {e.Message}");
                Console.WriteLine(e.ToString());
            }
```

이를 다음으로 교체한다:

```csharp
            /// <summary>
            /// [0.17.0]
            /// </summary>
            public static void LogException(string key, Exception e, string prefix)
            {
                if (string.IsNullOrEmpty(key))
                    Write($"{prefix}{e.GetType().Name} - {e.Message}");
                else
                    Write($"{prefix}{key}: {e.GetType().Name} - {e.Message}");
                Console.WriteLine(e.ToString());

                try
                {
                    var frame = new StackTrace(e, false).GetFrame(0)?.GetMethod();
                    var info = HarmonyDiagnostics.DescribePatches(frame);
                    if (!string.IsNullOrEmpty(info))
                        Write($"{prefix}  ↳ {info}");
                }
                catch
                {
                    // Diagnostics must never break the original log output.
                }
            }
```

- [ ] **Step 3: 빌드해서 컴파일 오류가 없는지 확인**

Run:
```bash
dotnet build UnityModManager/UnityModManager/UnityModManager.csproj -c Release -p:SolutionDir="D:/01_Code/10_CSharp/01_BetterUMM/"
```
Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add UnityModManager/UnityModManager/ModManager.cs
git commit -m "feat: Logger.LogException에서 Harmony 패치 정보 출력"
```

---

### Task 3: `Application.logMessageReceived` 구독 (uncaught 예외)

**Files:**
- Modify: `UnityModManager/UnityModManager/ModManager.cs:99-164` (`Initialize` 메소드)

- [ ] **Step 1: `Initialize()` 끝에 구독 코드 추가**

`UnityModManager/UnityModManager/ModManager.cs`의 `Initialize()` 메소드 끝부분은 다음과 같다 (Task 2의 using 추가 이후 기준, 약 158-164번째 줄):

```csharp
            Logger.Log($"Mods path: {modsPath}.");
            OldModsPath = modsPath;

            KeyBinding.Initialize();

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            return true;
        }
```

이를 다음으로 교체한다 (`Application.logMessageReceived` 구독 한 줄 추가):

```csharp
            Logger.Log($"Mods path: {modsPath}.");
            OldModsPath = modsPath;

            KeyBinding.Initialize();

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            Application.logMessageReceived += HarmonyDiagnostics.OnLogMessageReceived;

            return true;
        }
```

- [ ] **Step 2: 빌드해서 컴파일 오류가 없는지 확인**

Run:
```bash
dotnet build UnityModManager/UnityModManager/UnityModManager.csproj -c Release -p:SolutionDir="D:/01_Code/10_CSharp/01_BetterUMM/"
```
Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 3: 빌드 산출물이 BetterUMM 리소스로 복사됐는지 확인**

Run:
```bash
git status --short BetterUMM/Resources/UnityModManager/UnityModManager.dll
```
Expected: ` M BetterUMM/Resources/UnityModManager/UnityModManager.dll` (수정됨으로 표시)

- [ ] **Step 4: Commit**

```bash
git add UnityModManager/UnityModManager/ModManager.cs BetterUMM/Resources/UnityModManager/UnityModManager.dll
git commit -m "feat: Application.logMessageReceived 구독으로 uncaught 예외에도 Harmony 패치 정보 출력"
```

---

### Task 4: 수동 검증 (실제 게임에서 동작 확인)

이 Task는 코드 변경이 아니라 빌드 산출물(`BetterUMM/Resources/UnityModManager/UnityModManager.dll`)이 실제 게임에서 의도대로 동작하는지 확인하는 절차이다. 설계 문서(`docs/superpowers/specs/2026-06-13-harmony-error-tracing-design.md`)의 "검증 방법" 섹션에 따른 수동 테스트이다.

- [ ] **Step 1: 테스트용 모드 준비**

아래 두 파일로 구성된 임시 테스트 모드를 게임의 `Mods/` 폴더 아래 `HarmonyErrorTracingTest/`라는 새 폴더에 만든다 (이 폴더는 리포지토리에 커밋하지 않는다 - 로컬 검증용).

`Info.json`:
```json
{
  "Id": "HarmonyErrorTracingTest",
  "DisplayName": "Harmony Error Tracing Test",
  "Author": "local",
  "Version": "1.0.0",
  "AssemblyName": "HarmonyErrorTracingTest.dll",
  "EntryMethod": "HarmonyErrorTracingTest.Main.Load"
}
```

`Main.cs` (별도의 작은 클래스 라이브러리 프로젝트로 빌드, `0Harmony.dll`과 `UnityModManager.dll`을 참조):
```csharp
using HarmonyLib;
using UnityModManagerNet;

namespace HarmonyErrorTracingTest
{
    public static class Main
    {
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            // 예: 게임에서 자주 호출되는 Update성 메소드를 골라 Postfix에서 강제로 예외를 던진다.
            // harmony.Patch(
            //     AccessTools.Method(typeof(SomeGameClass), "SomeMethod"),
            //     postfix: new HarmonyMethod(typeof(Main), nameof(ThrowingPostfix)));
            return true;
        }

        // public static void ThrowingPostfix()
        // {
        //     throw new System.Exception("Intentional test exception");
        // }
    }
}
```

- [ ] **Step 2: 게임 실행 후 `Log.txt` 확인**

게임의 `<Game>_Data/Managed/UnityModManager/Log.txt`(또는 게임 설치 폴더 내 `Log.txt`)를 열어 다음과 같은 형태의 줄이 추가되었는지 확인한다:

```
[Manager] [Exception] Exception - Intentional test exception
[Manager]   ↳ Harmony patches on <SomeGameClass>.<SomeMethod>: Harmony Error Tracing Test (Postfix)
```

- [ ] **Step 3: 테스트 모드 제거**

검증이 끝나면 게임의 `Mods/HarmonyErrorTracingTest/` 폴더를 삭제한다 (리포지토리에는 포함되지 않으므로 git 작업 불필요).

---

## Self-Review 결과

- **스펙 커버리지**: 설계 문서의 Case A/B/C, Owner→모드 이름 매핑, Hook A(`LogException`)/Hook B(`Application.logMessageReceived`), try-catch에 의한 안전 처리, IL2CPP 제한사항(매칭 실패 시 조용히 무시) 모두 Task 1-3에서 구현됨. 수동 검증 절차는 Task 4에서 다룸.
- **타입 일관성**: `DescribePatches(MethodBase)`는 Task 1에서 정의되고 Task 2/3에서 동일한 시그니처로 호출됨. `HarmonyDiagnostics.OnLogMessageReceived(string, string, LogType)`은 `Application.logMessageReceived` 델리게이트 시그니처(`UnityEngine.Application.LogCallback`)와 일치.
- **빌드/배포**: 각 코드 변경 Task 뒤에 동일한 `dotnet build` 명령으로 검증하며, post-build target이 `BetterUMM/Resources/UnityModManager/UnityModManager.dll`을 자동 갱신함을 Task 3에서 명시적으로 확인.
