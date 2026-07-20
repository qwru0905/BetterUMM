# PatchService 크로스플랫폼 포팅 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows 전용인 `PatchService`를 `IPatchService`/`PatchServiceFactory` 패턴으로 추상화하고, Linux/macOS에서 Doorstop 패치를 수행하는 `UnixDoorstopPatchService`를 추가하여 BetterUMM이 세 플랫폼 모두에서 게임을 패치할 수 있도록 한다.

**Architecture:** `IPatchService` 인터페이스를 정의하고 `PatchServiceFactory.Create()`가 `OperatingSystem.IsWindows()/IsLinux()/IsMacOS()`로 분기해 `WindowsPatchService` 또는 `UnixDoorstopPatchService`를 생성한다. 기존 `PatchService.cs`의 코드는 `WindowsPatchService`로 이동하고, 공용 파일 유틸리티(`PatchFileOps`)와 ELF/`.app` 번들 파싱 헬퍼(`ElfBinaryInspector`, `MacAppBundleHelper`)는 별도 클래스로 추출한다. Doorstop 네이티브 리소스는 `Resources/Doorstop/{win,linux,osx,unix}/...` 구조로 재배치하고 `BetterUMM.csproj`에서 통째로 복사한다.

**Tech Stack:** .NET 8 / Avalonia / Mono.Cecil / xUnit (신규 테스트 프로젝트) / `File.SetUnixFileMode` (.NET 7+)

---

## File Structure Overview

- `BetterUMM.Tests/BetterUMM.Tests.csproj` — 신규 xUnit 테스트 프로젝트
- `BetterUMM/Services/Patching/IPatchService.cs` — 인터페이스 + `PatchStatus` enum
- `BetterUMM/Services/Patching/PatchServiceFactory.cs` — OS별 구현체 생성 팩토리
- `BetterUMM/Services/Patching/ElfBinaryInspector.cs` — ELF 바이너리 32/64비트 판별
- `BetterUMM/Services/Patching/MacAppBundleHelper.cs` — `.app` 번들의 `Info.plist` 파싱
- `BetterUMM/Services/Patching/PatchFileOps.cs` — 백업/복구/설정 파일 export 등 공용 파일 유틸 + `UmmConfig`
- `BetterUMM/Services/Patching/WindowsPatchService.cs` — 기존 `PatchService` 로직 이전 (Doorstop + Assembly Injection)
- `BetterUMM/Services/Patching/UnixDoorstopPatchService.cs` — Linux/macOS Doorstop 설치/제거
- `BetterUMM/Services/PatchService.cs` — **삭제** (Task 10)
- `BetterUMM/ViewModels/MainViewModel.cs` — `IPatchService` 사용, OS별 게임 선택 로직으로 교체
- `BetterUMM/BetterUMM.csproj` — 리소스 번들 블록 갱신
- `BetterUMM.slnx` — 테스트 프로젝트 추가
- `.gitignore` — `Resources/Doorstop/**` 예외 규칙 추가
- `BetterUMM/Resources/Doorstop/{win,linux,osx,unix}/...` — 재배치된 Doorstop 네이티브 리소스

---

### Task 1: 테스트 프로젝트 생성

**Files:**
- Create: `BetterUMM.Tests/BetterUMM.Tests.csproj`
- Create: `BetterUMM.Tests/SmokeTests.cs`
- Modify: `BetterUMM.slnx`

- [ ] **Step 1: 테스트 프로젝트 디렉터리/파일 생성**

`BetterUMM.Tests/BetterUMM.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\BetterUMM\BetterUMM.csproj" />
  </ItemGroup>

</Project>
```

`BetterUMM.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace BetterUMM.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void Smoke_AlwaysPasses()
        {
            Assert.True(true);
        }
    }
}
```

- [ ] **Step 2: `BetterUMM.slnx`에 테스트 프로젝트 등록**

`BetterUMM.slnx`를 다음과 같이 수정 (전체 파일):

```xml
<Solution>
  <Project Path="BetterUMM/BetterUMM.csproj" />
  <Project Path="BetterUMM.Tests/BetterUMM.Tests.csproj" />
  <Project Path="UnityModManager/UnityModManager/UnityModManager.csproj" />
</Solution>
```

- [ ] **Step 3: 빌드 및 테스트 실행으로 프로젝트 연결 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 4: 커밋**

```bash
git add BetterUMM.Tests/BetterUMM.Tests.csproj BetterUMM.Tests/SmokeTests.cs BetterUMM.slnx
git commit -m "test: BetterUMM.Tests xUnit 프로젝트 추가"
```

---

### Task 2: `ElfBinaryInspector` (TDD)

**Files:**
- Create: `BetterUMM/Services/Patching/ElfBinaryInspector.cs`
- Test: `BetterUMM.Tests/Services/Patching/ElfBinaryInspectorTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`BetterUMM.Tests/Services/Patching/ElfBinaryInspectorTests.cs`:

```csharp
using System;
using System.IO;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class ElfBinaryInspectorTests
    {
        [Fact]
        public void Is64Bit_ForElf64Header_ReturnsTrue()
        {
            string path = WriteTempFile(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0x02, 0x01, 0x01, 0x00, 0x00 });
            try
            {
                Assert.Equal(true, ElfBinaryInspector.Is64Bit(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Is64Bit_ForElf32Header_ReturnsFalse()
        {
            string path = WriteTempFile(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0x01, 0x01, 0x01, 0x00, 0x00 });
            try
            {
                Assert.Equal(false, ElfBinaryInspector.Is64Bit(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Is64Bit_ForNonElfHeader_ReturnsNull()
        {
            string path = WriteTempFile(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00 });
            try
            {
                Assert.Null(ElfBinaryInspector.Is64Bit(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Is64Bit_ForMissingFile_ReturnsNull()
        {
            string path = Path.Combine(Path.GetTempPath(), $"betterumm-elf-missing-{Guid.NewGuid():N}.bin");
            Assert.Null(ElfBinaryInspector.Is64Bit(path));
        }

        private static string WriteTempFile(byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(), $"betterumm-elf-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, content);
            return path;
        }
    }
}
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter ElfBinaryInspectorTests`
Expected: FAIL with compile error "The type or namespace name 'ElfBinaryInspector' could not be found"

- [ ] **Step 3: `ElfBinaryInspector` 구현**

`BetterUMM/Services/Patching/ElfBinaryInspector.cs`:

```csharp
using System;
using System.IO;
using BetterUMM.Services;

namespace BetterUMM.Services.Patching
{
    public static class ElfBinaryInspector
    {
        public static bool? Is64Bit(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                Span<byte> header = stackalloc byte[5];
                if (stream.Read(header) != header.Length) return null;
                if (header[0] != 0x7F || header[1] != (byte)'E' || header[2] != (byte)'L' || header[3] != (byte)'F')
                    return null;
                return header[4] switch
                {
                    1 => false,
                    2 => true,
                    _ => null
                };
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"ElfBinaryInspector.Is64Bit: {filePath}");
                return null;
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter ElfBinaryInspectorTests`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 5: 커밋**

```bash
git add BetterUMM/Services/Patching/ElfBinaryInspector.cs BetterUMM.Tests/Services/Patching/ElfBinaryInspectorTests.cs
git commit -m "feat: ELF 바이너리 32/64비트 판별 ElfBinaryInspector 추가"
```

---

### Task 3: `MacAppBundleHelper` (TDD)

**Files:**
- Create: `BetterUMM/Services/Patching/MacAppBundleHelper.cs`
- Test: `BetterUMM.Tests/Services/Patching/MacAppBundleHelperTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`BetterUMM.Tests/Services/Patching/MacAppBundleHelperTests.cs`:

```csharp
using System;
using System.IO;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class MacAppBundleHelperTests
    {
        private const string ValidPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "    <key>CFBundleExecutable</key>\n" +
            "    <string>MyGame</string>\n" +
            "</dict>\n" +
            "</plist>\n";

        [Fact]
        public void ResolveExecutablePath_ForValidBundle_ReturnsExecutablePath()
        {
            string bundlePath = CreateBundle("MyGame.app", ValidPlist);
            try
            {
                string result = MacAppBundleHelper.ResolveExecutablePath(bundlePath);
                Assert.Equal(Path.Combine(bundlePath, "Contents", "MacOS", "MyGame"), result);
            }
            finally
            {
                Directory.Delete(bundlePath, true);
            }
        }

        [Fact]
        public void ResolveExecutablePath_ForMissingInfoPlist_ThrowsFileNotFoundException()
        {
            string bundlePath = Path.Combine(Path.GetTempPath(), $"betterumm-bundle-{Guid.NewGuid():N}", "Empty.app");
            Directory.CreateDirectory(Path.Combine(bundlePath, "Contents"));
            try
            {
                Assert.Throws<FileNotFoundException>(() => MacAppBundleHelper.ResolveExecutablePath(bundlePath));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(bundlePath)!, true);
            }
        }

        [Fact]
        public void ResolveExecutablePath_ForPlistWithoutExecutableKey_ThrowsInvalidDataException()
        {
            const string plistWithoutKey =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<plist version=\"1.0\">\n" +
                "<dict>\n" +
                "    <key>CFBundleName</key>\n" +
                "    <string>MyGame</string>\n" +
                "</dict>\n" +
                "</plist>\n";
            string bundlePath = CreateBundle("NoExec.app", plistWithoutKey);
            try
            {
                Assert.Throws<InvalidDataException>(() => MacAppBundleHelper.ResolveExecutablePath(bundlePath));
            }
            finally
            {
                Directory.Delete(bundlePath, true);
            }
        }

        [Fact]
        public void ResolveExecutablePath_ForBinaryPlist_ThrowsNotSupportedException()
        {
            string bundlePath = Path.Combine(Path.GetTempPath(), $"betterumm-bundle-{Guid.NewGuid():N}", "Binary.app");
            Directory.CreateDirectory(Path.Combine(bundlePath, "Contents"));
            File.WriteAllBytes(Path.Combine(bundlePath, "Contents", "Info.plist"), new byte[] { (byte)'b', (byte)'p', (byte)'l', (byte)'i', (byte)'s', (byte)'t', 0x30, 0x30 });
            try
            {
                Assert.Throws<NotSupportedException>(() => MacAppBundleHelper.ResolveExecutablePath(bundlePath));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(bundlePath)!, true);
            }
        }

        private static string CreateBundle(string bundleName, string plistContent)
        {
            string root = Path.Combine(Path.GetTempPath(), $"betterumm-bundle-{Guid.NewGuid():N}");
            string bundlePath = Path.Combine(root, bundleName);
            Directory.CreateDirectory(Path.Combine(bundlePath, "Contents"));
            File.WriteAllText(Path.Combine(bundlePath, "Contents", "Info.plist"), plistContent);
            return bundlePath;
        }
    }
}
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter MacAppBundleHelperTests`
Expected: FAIL with compile error "The type or namespace name 'MacAppBundleHelper' could not be found"

- [ ] **Step 3: `MacAppBundleHelper` 구현**

`BetterUMM/Services/Patching/MacAppBundleHelper.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace BetterUMM.Services.Patching
{
    public static class MacAppBundleHelper
    {
        private const string BinaryPlistMagic = "bplist";

        public static string ResolveExecutablePath(string appBundlePath)
        {
            string infoPlistPath = Path.Combine(appBundlePath, "Contents", "Info.plist");
            if (!File.Exists(infoPlistPath))
                throw new FileNotFoundException($"Info.plist을 찾을 수 없습니다: {infoPlistPath}", infoPlistPath);

            byte[] header = new byte[6];
            using (var headerStream = File.OpenRead(infoPlistPath))
            {
                int read = headerStream.Read(header, 0, header.Length);
                if (read == header.Length && Encoding.ASCII.GetString(header) == BinaryPlistMagic)
                    throw new NotSupportedException($"바이너리 plist 형식은 지원하지 않습니다: {infoPlistPath}");
            }

            XDocument document = XDocument.Load(infoPlistPath);
            XElement? dict = document.Root?.Element("dict");
            if (dict == null)
                throw new InvalidDataException($"Info.plist에서 <dict>를 찾을 수 없습니다: {infoPlistPath}");

            string? executableName = null;
            var children = dict.Elements().ToArray();
            for (int i = 0; i < children.Length - 1; i++)
            {
                if (children[i].Name == "key" && children[i].Value == "CFBundleExecutable")
                {
                    executableName = children[i + 1].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(executableName))
                throw new InvalidDataException($"Info.plist에 CFBundleExecutable 키가 없습니다: {infoPlistPath}");

            return Path.Combine(appBundlePath, "Contents", "MacOS", executableName);
        }
    }
}
```

`children.Elements().ToArray()`를 사용하므로 파일 상단에 `using System.Linq;`도 추가해야 한다. 위 using 목록을 다음으로 교체:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter MacAppBundleHelperTests`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 5: 커밋**

```bash
git add BetterUMM/Services/Patching/MacAppBundleHelper.cs BetterUMM.Tests/Services/Patching/MacAppBundleHelperTests.cs
git commit -m "feat: macOS .app 번들 Info.plist 파싱 MacAppBundleHelper 추가"
```

---

### Task 4: `PatchFileOps`/`UmmConfig` 추출 (TDD)

**Files:**
- Create: `BetterUMM/Services/Patching/PatchFileOps.cs`
- Test: `BetterUMM.Tests/Services/Patching/PatchFileOpsTests.cs`
- Reference (코드 출처, 수정하지 않음): `BetterUMM/Services/PatchService.cs:98-115, 523-546, 549-570`

이 태스크는 `PatchService.cs`의 `ExportConfig`/`MakeBackup`/`RestoreBackups`/`DeleteBackups`/`TryDelete`/`UmmConfig`를 **시그니처와 동작을 그대로 유지한 채** `internal`/`private`였던 것을 `public`으로 바꿔 새 파일로 옮기는 작업이다 (원본은 Task 10에서 파일 전체 삭제 시 함께 제거됨 — 지금은 새 위치에 복사만 한다). 백업 추적 자료구조는 원본과 동일하게 **`List<string>`** (백업 파일 경로는 `원본경로 + ".bak"`로 항상 유도) 이다 — 튜플 리스트가 아니다.

> **스펙과의 의도적 차이:** 스펙은 `internal static` 헬퍼로의 추출을 제안하지만, `BetterUMM.Tests`가 `InternalsVisibleTo` 선언 없이 직접 round-trip 테스트를 수행할 수 있도록 `public static class`/`public class`로 공개한다. 동작·시그니처는 스펙이 의도한 바와 동일하며, 가시성만 테스트 용이성을 위해 한 단계 넓힌 것이다.

- [ ] **Step 1: 실패하는 테스트 작성**

`BetterUMM.Tests/Services/Patching/PatchFileOpsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using BetterUMM.Models;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class PatchFileOpsTests
    {
        [Fact]
        public void MakeBackup_CreatesBakFileAndTracksOriginalPath_ThenRestoreMovesItBack()
        {
            string dir = CreateTempDir();
            try
            {
                string original = Path.Combine(dir, "config.ini");
                File.WriteAllText(original, "original");
                var tracked = new List<string>();

                PatchFileOps.MakeBackup(original, tracked);
                Assert.Single(tracked);
                Assert.Equal(original, tracked[0]);
                Assert.True(File.Exists(original + ".bak"));

                File.WriteAllText(original, "modified");
                PatchFileOps.RestoreBackups(tracked);

                Assert.Equal("original", File.ReadAllText(original));
                Assert.False(File.Exists(original + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MakeBackup_ForMissingOriginal_DoesNotTrackOrCreateBackup()
        {
            string dir = CreateTempDir();
            try
            {
                string original = Path.Combine(dir, "missing.ini");
                var tracked = new List<string>();

                PatchFileOps.MakeBackup(original, tracked);

                Assert.Empty(tracked);
                Assert.False(File.Exists(original + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void DeleteBackups_RemovesBakFilesForTrackedPaths()
        {
            string dir = CreateTempDir();
            try
            {
                string original = Path.Combine(dir, "config.ini");
                File.WriteAllText(original, "original");
                var tracked = new List<string>();
                PatchFileOps.MakeBackup(original, tracked);

                PatchFileOps.DeleteBackups(tracked);

                Assert.False(File.Exists(original + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TryDelete_ForExistingFile_DeletesIt()
        {
            string dir = CreateTempDir();
            try
            {
                string path = Path.Combine(dir, "victim.txt");
                File.WriteAllText(path, "data");

                PatchFileOps.TryDelete(path);

                Assert.False(File.Exists(path));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TryDelete_ForMissingFile_DoesNotThrow()
        {
            string path = Path.Combine(Path.GetTempPath(), $"betterumm-missing-{Guid.NewGuid():N}.txt");
            var exception = Record.Exception(() => PatchFileOps.TryDelete(path));
            Assert.Null(exception);
        }

        [Fact]
        public void ExportConfig_WritesXmlFileWithGameNameAndFolder()
        {
            string dir = CreateTempDir();
            try
            {
                string configPath = Path.Combine(dir, "Config.xml");
                var game = new GameInfo
                {
                    Name = "TestGame",
                    Path = "C:\\Games\\TestGame\\TestGame.exe",
                    GameDataPath = "C:\\Games\\TestGame\\TestGame_Data",
                    AssemblyName = "Assembly-CSharp.dll",
                    PatchTarget = string.Empty,
                    Folder = "TestGameFolder",
                    ModsDirectory = "Mods",
                    ModInfo = "Info.json",
                    GameExe = "TestGame.exe",
                    EntryPoint = string.Empty,
                    StartingPoint = string.Empty,
                    UIStartingPoint = string.Empty,
                    OldPatchTarget = string.Empty,
                    GameVersionPoint = string.Empty,
                    MinimalManagerVersion = string.Empty,
                    HarmonyVersion = string.Empty
                };

                PatchFileOps.ExportConfig(game, configPath);

                Assert.True(File.Exists(configPath));
                string content = File.ReadAllText(configPath);
                Assert.Contains("TestGame", content);
                Assert.Contains("TestGameFolder", content);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"betterumm-fileops-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter PatchFileOpsTests`
Expected: FAIL with compile error "The type or namespace name 'PatchFileOps' could not be found"

- [ ] **Step 3: `PatchFileOps`/`UmmConfig` 구현**

`BetterUMM/Services/PatchService.cs`의 `ExportConfig` (98-115줄), `MakeBackup`/`RestoreBackups`/`DeleteBackups`/`TryDelete` (523-546줄), `UmmConfig` (549-570줄)를 **그대로** (필드/시그니처 변경 없이) 옮기되 접근 제한자만 `public`으로 바꾼다.

`BetterUMM/Services/Patching/PatchFileOps.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public static class PatchFileOps
    {
        public static void ExportConfig(GameInfo game, string destPath)
        {
            var config = new UmmConfig
            {
                Name = game.Name,
                Folder = game.Folder,
                ModsDirectory = game.ModsDirectory,
                ModInfo = game.ModInfo,
                GameExe = game.GameExe,
                EntryPoint = game.EntryPoint,
                StartingPoint = game.StartingPoint,
                UIStartingPoint = game.UIStartingPoint,
                MinimalManagerVersion = game.MinimalManagerVersion,
            };
            var serializer = new XmlSerializer(typeof(UmmConfig));
            using var writer = new StreamWriter(destPath);
            serializer.Serialize(writer, config);
        }

        public static void MakeBackup(string path, List<string> tracked)
        {
            if (!File.Exists(path)) return;
            File.Copy(path, path + ".bak", true);
            tracked.Add(path);
        }

        public static void RestoreBackups(List<string> tracked)
        {
            foreach (var path in tracked)
                if (File.Exists(path + ".bak"))
                    File.Move(path + ".bak", path, true);
        }

        public static void DeleteBackups(List<string> tracked)
        {
            foreach (var path in tracked)
                TryDelete(path + ".bak");
        }

        public static void TryDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [XmlRoot("Config")]
    public class UmmConfig
    {
        [XmlAttribute("Name")]
        public string Name { get; set; } = string.Empty;
        [XmlElement("Folder")]
        public string Folder { get; set; } = string.Empty;
        [XmlElement("ModsDirectory")]
        public string ModsDirectory { get; set; } = "Mods";
        [XmlElement("ModInfo")]
        public string ModInfo { get; set; } = "Info.json";
        [XmlElement("GameExe")]
        public string GameExe { get; set; } = string.Empty;
        [XmlElement("EntryPoint")]
        public string EntryPoint { get; set; } = string.Empty;
        [XmlElement("StartingPoint")]
        public string StartingPoint { get; set; } = string.Empty;
        [XmlElement("UIStartingPoint")]
        public string UIStartingPoint { get; set; } = string.Empty;
        [XmlElement("MinimalManagerVersion")]
        public string MinimalManagerVersion { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter PatchFileOpsTests`
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0`

- [ ] **Step 5: 커밋**

```bash
git add BetterUMM/Services/Patching/PatchFileOps.cs BetterUMM.Tests/Services/Patching/PatchFileOpsTests.cs
git commit -m "refactor: 백업/설정 파일 유틸리티를 PatchFileOps로 추출"
```

---

### Task 5: `IPatchService`/`PatchServiceFactory`

**Files:**
- Create: `BetterUMM/Services/Patching/IPatchService.cs`
- Create: `BetterUMM/Services/Patching/PatchServiceFactory.cs`
- Test: `BetterUMM.Tests/Services/Patching/PatchServiceFactoryTests.cs`

- [ ] **Step 1: `IPatchService` 인터페이스 작성**

`BetterUMM/Services/Patching/IPatchService.cs`:

```csharp
using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public enum PatchStatus
    {
        NotInstalled,
        AssemblyInjection,
        Doorstop
    }

    public interface IPatchService
    {
        PatchStatus GetPatchStatus(GameInfo game);
        bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths);
        bool RemoveDoorstop(GameInfo game);
        bool InstallAssembly(GameInfo game, string[] ummLibraryPaths);
        bool RemoveAssembly(GameInfo game);
    }
}
```

- [ ] **Step 2: 실패하는 팩토리 테스트 작성**

`BetterUMM.Tests/Services/Patching/PatchServiceFactoryTests.cs`:

```csharp
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class PatchServiceFactoryTests
    {
        [Fact]
        public void Create_ReturnsNonNullPlatformAppropriateService()
        {
            IPatchService service = PatchServiceFactory.Create();

            Assert.NotNull(service);
            if (System.OperatingSystem.IsWindows())
                Assert.IsType<WindowsPatchService>(service);
            else
                Assert.IsType<UnixDoorstopPatchService>(service);
        }
    }
}
```

- [ ] **Step 3: 테스트 실행하여 실패 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter PatchServiceFactoryTests`
Expected: FAIL with compile error — `WindowsPatchService`/`UnixDoorstopPatchService`/`PatchServiceFactory` 타입이 아직 없음

- [ ] **Step 4: `PatchServiceFactory` 구현**

이 단계에서는 `WindowsPatchService`와 `UnixDoorstopPatchService`가 아직 존재하지 않으므로, 컴파일이 통과하도록 두 클래스의 빈 골격을 함께 만든다 (Task 7, 8에서 본 구현으로 대체됨).

`BetterUMM/Services/Patching/PatchServiceFactory.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace BetterUMM.Services.Patching
{
    public static class PatchServiceFactory
    {
        public static IPatchService Create()
        {
            if (OperatingSystem.IsWindows()) return new WindowsPatchService();
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new UnixDoorstopPatchService();
            throw new PlatformNotSupportedException($"지원하지 않는 운영체제입니다: {RuntimeInformation.OSDescription}");
        }
    }
}
```

`BetterUMM/Services/Patching/WindowsPatchService.cs` (임시 골격 — Task 7에서 전체 구현으로 교체):

```csharp
using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public class WindowsPatchService : IPatchService
    {
        public PatchStatus GetPatchStatus(GameInfo game) => throw new System.NotImplementedException();
        public bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths) => throw new System.NotImplementedException();
        public bool RemoveDoorstop(GameInfo game) => throw new System.NotImplementedException();
        public bool InstallAssembly(GameInfo game, string[] ummLibraryPaths) => throw new System.NotImplementedException();
        public bool RemoveAssembly(GameInfo game) => throw new System.NotImplementedException();
    }
}
```

`BetterUMM/Services/Patching/UnixDoorstopPatchService.cs` (임시 골격 — Task 8에서 전체 구현으로 교체):

```csharp
using BetterUMM.Models;

namespace BetterUMM.Services.Patching
{
    public class UnixDoorstopPatchService : IPatchService
    {
        public PatchStatus GetPatchStatus(GameInfo game) => throw new System.NotImplementedException();
        public bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths) => throw new System.NotImplementedException();
        public bool RemoveDoorstop(GameInfo game) => throw new System.NotImplementedException();
        public bool InstallAssembly(GameInfo game, string[] ummLibraryPaths) => throw new System.NotImplementedException();
        public bool RemoveAssembly(GameInfo game) => throw new System.NotImplementedException();
    }
}
```

- [ ] **Step 5: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter PatchServiceFactoryTests`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 6: 커밋**

```bash
git add BetterUMM/Services/Patching/IPatchService.cs BetterUMM/Services/Patching/PatchServiceFactory.cs BetterUMM/Services/Patching/WindowsPatchService.cs BetterUMM/Services/Patching/UnixDoorstopPatchService.cs BetterUMM.Tests/Services/Patching/PatchServiceFactoryTests.cs
git commit -m "feat: IPatchService/PatchServiceFactory 추가 및 OS별 구현체 골격 생성"
```

---

### Task 6: Doorstop 리소스 재배치 및 번들링

**Files:**
- Move: `BetterUMM/Resources/winhttp_x64.dll` → `BetterUMM/Resources/Doorstop/win/x64/winhttp.dll`
- Move: `BetterUMM/Resources/winhttp_x86.dll` → `BetterUMM/Resources/Doorstop/win/x86/winhttp.dll`
- Create: `BetterUMM/Resources/Doorstop/linux/x64/libdoorstop.so`
- Create: `BetterUMM/Resources/Doorstop/linux/x86/libdoorstop.so`
- Create: `BetterUMM/Resources/Doorstop/osx/libdoorstop.dylib`
- Create: `BetterUMM/Resources/Doorstop/unix/run.sh`
- Modify: `.gitignore`
- Modify: `BetterUMM/BetterUMM.csproj`

이 태스크는 공식 [NeighTools/UnityDoorstop v4.5.0](https://github.com/NeighTools/UnityDoorstop/releases/tag/v4.5.0) 릴리스 자산을 다운로드하여 리소스를 재배치한다. 라이선스는 LGPL-2.1로, 바이너리 번들링이 허용된다.

- [ ] **Step 1: 새 디렉터리 구조로 기존 Windows DLL 이동**

```bash
mkdir -p BetterUMM/Resources/Doorstop/win/x64 BetterUMM/Resources/Doorstop/win/x86
git mv BetterUMM/Resources/winhttp_x64.dll BetterUMM/Resources/Doorstop/win/x64/winhttp.dll
git mv BetterUMM/Resources/winhttp_x86.dll BetterUMM/Resources/Doorstop/win/x86/winhttp.dll
```

- [ ] **Step 2: 공식 Linux/macOS 릴리스 자산 다운로드 및 추출**

```bash
mkdir -p /tmp/doorstop-release
cd /tmp/doorstop-release
gh release download v4.5.0 --repo NeighTools/UnityDoorstop \
  --pattern "doorstop_linux_release_4.5.0.zip" \
  --pattern "doorstop_macos_release_4.5.0.zip" \
  --pattern "doorstop_win_release_4.5.0.zip"
unzip -o doorstop_linux_release_4.5.0.zip -d linux
unzip -o doorstop_macos_release_4.5.0.zip -d macos
```

(win 자산은 기존 `winhttp_x64/x86.dll`과 동일하므로 다운로드만 받고 사용하지 않는다 — 버전 일치 여부 확인용으로만 둔다.)

- [ ] **Step 3: 추출한 파일을 리소스 디렉터리로 복사**

```bash
cd D:/01_Code/10_CSharp/01_BetterUMM
mkdir -p BetterUMM/Resources/Doorstop/linux/x64 BetterUMM/Resources/Doorstop/linux/x86 \
         BetterUMM/Resources/Doorstop/osx BetterUMM/Resources/Doorstop/unix

cp /tmp/doorstop-release/linux/x64/libdoorstop.so BetterUMM/Resources/Doorstop/linux/x64/libdoorstop.so
cp /tmp/doorstop-release/linux/x86/libdoorstop.so BetterUMM/Resources/Doorstop/linux/x86/libdoorstop.so
cp /tmp/doorstop-release/macos/universal/libdoorstop.dylib BetterUMM/Resources/Doorstop/osx/libdoorstop.dylib
cp /tmp/doorstop-release/linux/x64/run.sh BetterUMM/Resources/Doorstop/unix/run.sh
```

(공식 `run.sh`는 x64/x86 빌드 간에 동일하므로 x64 쪽 사본을 사용한다.)

- [ ] **Step 4: `.gitignore`에 Doorstop 리소스 예외 규칙 추가**

`.gitignore` 파일은 VisualStudio 템플릿 기본 규칙으로 `x64/`, `x86/` 같은 디렉터리를 어디에 있든 무시한다 (루트 근처 11-12번째 줄). 이 규칙이 `Resources/Doorstop/{win,linux}/{x64,x86}/` 폴더를 그대로 삼켜버리므로, 파일 끝부분(또는 `x64/`/`x86/` 규칙 바로 아래)에 다음 negation 규칙을 추가한다:

```
# Doorstop 번들 리소스는 위 x64/x86 빌드 출력 무시 규칙에서 예외 처리
!BetterUMM/Resources/Doorstop/
!BetterUMM/Resources/Doorstop/**
```

- [ ] **Step 5: 추가될 파일 확인 (dry-run)**

```bash
git add -A -n BetterUMM/Resources/Doorstop
```

Expected: 출력에 다음 6개 파일이 모두 `add` 대상으로 나타나야 한다:
- `BetterUMM/Resources/Doorstop/win/x64/winhttp.dll`
- `BetterUMM/Resources/Doorstop/win/x86/winhttp.dll`
- `BetterUMM/Resources/Doorstop/linux/x64/libdoorstop.so`
- `BetterUMM/Resources/Doorstop/linux/x86/libdoorstop.so`
- `BetterUMM/Resources/Doorstop/osx/libdoorstop.dylib`
- `BetterUMM/Resources/Doorstop/unix/run.sh`

만약 일부 파일이 빠져 있다면 `.gitignore` negation 규칙이 잘못된 것이므로 Step 4로 돌아가 경로를 점검한다.

- [ ] **Step 6: `BetterUMM.csproj` 리소스 블록을 새 구조에 맞게 갱신**

`BetterUMM/BetterUMM.csproj`에서 기존의 `Resources\winhttp_x64.dll`/`Resources\winhttp_x86.dll`에 대한 두 개의 `<None Update>` 블록을 찾는다:

```xml
    <None Update="Resources\winhttp_x64.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>winhttp_x64.dll</Link>
    </None>
    <None Update="Resources\winhttp_x86.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>winhttp_x86.dll</Link>
    </None>
```

이 두 블록을 다음 하나의 glob 블록으로 교체한다 (기존 `Resources\UnityModManager\**` 블록과 동일한 패턴):

```xml
    <None Include="Resources\Doorstop\**">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>Doorstop\%(RecursiveDir)%(FileName)%(Extension)</Link>
    </None>
```

- [ ] **Step 7: 빌드하여 리소스가 출력 디렉터리로 복사되는지 확인**

Run: `dotnet build BetterUMM/BetterUMM.csproj -c Debug`
Expected: 빌드 성공, `BetterUMM/bin/Debug/net8.0/Doorstop/win/x64/winhttp.dll` 등 6개 파일이 출력 디렉터리에 존재

확인 명령: `ls -R BetterUMM/bin/Debug/net8.0/Doorstop`

- [ ] **Step 8: 리소스 레이아웃이 빌드 출력에 포함되는지 검증하는 자동화 테스트 작성**

`BetterUMM.Tests`는 `BetterUMM.csproj`를 `ProjectReference`로 참조하므로, `CopyToOutputDirectory`로 표시된 리소스는 테스트 프로젝트의 출력 디렉터리(`AppDomain.CurrentDomain.BaseDirectory`)에도 복사된다. 이를 이용해 6개 네이티브 자산이 실제로 복사되었는지 검증한다.

`BetterUMM.Tests/Services/Patching/DoorstopResourceLayoutTests.cs`:

```csharp
using System;
using System.IO;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class DoorstopResourceLayoutTests
    {
        [Theory]
        [InlineData("win", "x64", "winhttp.dll")]
        [InlineData("win", "x86", "winhttp.dll")]
        [InlineData("linux", "x64", "libdoorstop.so")]
        [InlineData("linux", "x86", "libdoorstop.so")]
        [InlineData("osx", "", "libdoorstop.dylib")]
        [InlineData("unix", "", "run.sh")]
        public void DoorstopResource_IsCopiedToOutputDirectory(string platform, string arch, string fileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = string.IsNullOrEmpty(arch)
                ? Path.Combine(baseDir, "Doorstop", platform, fileName)
                : Path.Combine(baseDir, "Doorstop", platform, arch, fileName);

            Assert.True(File.Exists(path), $"Expected resource not found at: {path}");
        }
    }
}
```

- [ ] **Step 9: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter DoorstopResourceLayoutTests`
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0`

만약 실패한다면 Step 6의 `<None Include="Resources\Doorstop\**">` 블록이 `BetterUMM.csproj`에 정확히 추가되었는지, 그리고 `dotnet build`를 다시 실행했는지 확인한다 (`ProjectReference`를 통한 리소스 전파는 참조 프로젝트가 먼저 빌드된 후에만 반영됨).

- [ ] **Step 10: 커밋**

```bash
git add BetterUMM/Resources/Doorstop .gitignore BetterUMM/BetterUMM.csproj BetterUMM.Tests/Services/Patching/DoorstopResourceLayoutTests.cs
git commit -m "build: Doorstop 네이티브 리소스를 Resources/Doorstop/{win,linux,osx,unix} 구조로 재배치"
```

---

### Task 7: `WindowsPatchService` — 기존 로직 이전

**Files:**
- Modify: `BetterUMM/Services/Patching/WindowsPatchService.cs`
- Reference (이전 대상 코드, 읽기 전용): `BetterUMM/Services/PatchService.cs` (전체 571줄)

기존 `PatchService.cs`의 모든 멤버를 `WindowsPatchService`로 옮기고 `IPatchService`를 구현하도록 어댑팅한다. 변경점은 다음과 같다:

1. `enum PatchStatus`는 더 이상 이 파일에 정의하지 않는다 (Task 5에서 `IPatchService.cs`로 이동됨) — `using BetterUMM.Services.Patching;`으로 충분.
2. `MakeBackup`/`RestoreBackups`/`DeleteBackups`/`TryDelete`/`ExportConfig`/`UmmConfig` 호출은 `PatchFileOps.*`를 사용한다.
3. `InstallDoorstop`의 시그니처를 `bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths)`로 변경하고, 기존에 매개변수로 받던 `doorstopX64Path`/`doorstopX86Path`를 메서드 내부에서 직접 계산한다:
   ```csharp
   string doorstopBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Doorstop", "win");
   string doorstopX64Path = Path.Combine(doorstopBaseDir, "x64", "winhttp.dll");
   string doorstopX86Path = Path.Combine(doorstopBaseDir, "x86", "winhttp.dll");
   ```
   이후 로직(아키텍처 판별 → DLL 선택 → 복사 → config 작성 등)은 기존과 동일하게 유지한다.

- [ ] **Step 1: `WindowsPatchService.cs`를 기존 `PatchService.cs` 로직으로 전체 작성**

`BetterUMM/Services/Patching/WindowsPatchService.cs`를 다음 전체 코드로 작성한다 (기존 `PatchService.cs`의 모든 로직을 포함하며, `InstallDoorstop`의 시그니처와 내부 경로 계산만 변경되고 백업 유틸 호출은 `PatchFileOps.*`로 교체됨):

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterUMM.Models;
using BetterUMM.Services;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BetterUMM.Services.Patching
{
    public class WindowsPatchService : IPatchService
    {
        private const string StarterTypeName = "UnityModManagerStarter";
        private const string StarterNamespace = "Injection";
        private const string UmmSubDir = "UnityModManager";
        private const string UmmDllName = "UnityModManager.dll";
        private const string DoorstopConfigFile = "doorstop_config.ini";
        private const string DoorstopDllFile = "winhttp.dll";

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

        public bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths)
        {
            string doorstopBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Doorstop", "win");
            string doorstopX64Path = Path.Combine(doorstopBaseDir, "x64", "winhttp.dll");
            string doorstopX86Path = Path.Combine(doorstopBaseDir, "x86", "winhttp.dll");

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

                PatchFileOps.MakeBackup(doorstopPath, backups);
                PatchFileOps.MakeBackup(configPath, backups);
                PatchFileOps.MakeBackup(gameConfigPath, backups);

                File.Copy(srcDll, doorstopPath, true);

                string dataFolderName = Path.GetFileName(game.GameDataPath);
                string relTarget = Path.Combine(dataFolderName, "Managed", UmmSubDir, UmmDllName);
                File.WriteAllText(configPath,
                    $"[General]{Environment.NewLine}enabled = true{Environment.NewLine}target_assembly = {relTarget}");

                foreach (var lib in ummLibraryPaths)
                {
                    string dest = Path.Combine(ummDir, Path.GetFileName(lib));
                    File.Copy(lib, dest, true);
                }

                PatchFileOps.ExportConfig(game, gameConfigPath);

                PatchFileOps.DeleteBackups(backups);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "InstallDoorstop");
                PatchFileOps.RestoreBackups(backups);
                return false;
            }
        }

        public bool RemoveDoorstop(GameInfo game)
        {
            string gameRoot = Path.GetDirectoryName(game.Path)!;
            string ummDir = Path.Combine(game.GameDataPath, "Managed", UmmSubDir);

            try
            {
                PatchFileOps.TryDelete(Path.Combine(gameRoot, DoorstopDllFile));
                PatchFileOps.TryDelete(Path.Combine(gameRoot, DoorstopConfigFile));
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

        public bool InstallAssembly(GameInfo game, string[] ummLibraryPaths)
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
                PatchFileOps.MakeBackup(assemblyPath, backups);

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

                foreach (var lib in ummLibraryPaths)
                    File.Copy(lib, Path.Combine(ummDir, Path.GetFileName(lib)), true);

                PatchFileOps.DeleteBackups(backups);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "InstallAssembly");
                PatchFileOps.RestoreBackups(backups);
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
                if (File.Exists(originalPath))
                {
                    File.Copy(originalPath, assemblyPath, overwrite: true);
                    File.Delete(originalPath);
                    LoggerService.Log($"Restored original assembly from {originalPath}");
                }
                else
                {
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

            il.Emit(OpCodes.Call, refGetExecAsm);
            il.Emit(OpCodes.Callvirt, refGetLocation);
            il.Emit(OpCodes.Stloc, vars[0]);

            il.Emit(OpCodes.Ldloc, vars[0]);
            il.Emit(OpCodes.Call, refGetDirName);
            il.Emit(OpCodes.Stloc, vars[1]);

            il.Emit(OpCodes.Ldloc, vars[1]);
            il.Emit(OpCodes.Ldstr, "UnityModManager");
            il.Emit(OpCodes.Ldstr, "UnityModManager.dll");
            il.Emit(OpCodes.Call, refCombine3);
            il.Emit(OpCodes.Stloc, vars[2]);

            il.Emit(OpCodes.Ldloc, vars[2]);
            il.Emit(OpCodes.Call, refFileExists);
            il.Emit(OpCodes.Brfalse, ret);

            il.Emit(OpCodes.Ldloc, vars[2]);
            il.Emit(OpCodes.Call, refLoadFile);
            il.Emit(OpCodes.Stloc, vars[3]);

            il.Emit(OpCodes.Ldloc, vars[3]);
            il.Emit(OpCodes.Ldstr, "UnityModManagerNet.Injector");
            il.Emit(OpCodes.Callvirt, refAsmGetType);
            il.Emit(OpCodes.Stloc, vars[4]);

            il.Emit(OpCodes.Ldloc, vars[4]);
            il.Emit(OpCodes.Brfalse, ret);

            il.Emit(OpCodes.Ldloc, vars[4]);
            il.Emit(OpCodes.Ldstr, "Run");
            il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Static));
            il.Emit(OpCodes.Callvirt, refTypeGetMethod);
            il.Emit(OpCodes.Stloc, vars[5]);

            il.Emit(OpCodes.Ldloc, vars[5]);
            il.Emit(OpCodes.Brfalse, ret);

            il.Emit(OpCodes.Ldloc, vars[5]);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, refObjType);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, refBoolType);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, refInvoke);
            il.Emit(OpCodes.Pop);

            il.Append(ret);

            type.Methods.Add(method);
            return type;
        }

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
            string cecilName = methodName == "cctor" ? ".cctor"
                             : methodName == "ctor"  ? ".ctor"
                             : methodName;

            foreach (var module in assembly.Modules)
            {
                var type = module.Types.FirstOrDefault(t => t.FullName == typeName)
                    ?? module.Types.FirstOrDefault(t => t.Name == typeName)
                    ?? module.Types.SelectMany(t => t.NestedTypes)
                              .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

                if (type != null)
                    return type.Methods.FirstOrDefault(m => m.Name == cecilName)
                        ?? type.Methods.FirstOrDefault(m => m.Name == methodName);
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

        private static bool? IsExecutable64Bit(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt16() != 0x5A4D) return null;
                stream.Seek(60, SeekOrigin.Begin);
                stream.Seek(reader.ReadInt32(), SeekOrigin.Begin);
                if (reader.ReadUInt32() != 0x00004550) return null;
                var machine = reader.ReadUInt16();
                return machine == 0x8664 || machine == 0x0200;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, $"IsExecutable64Bit: {filePath}");
                return null;
            }
        }
    }
}
```

> **차이점 요약 (기존 `PatchService.cs` 대비):** ① 클래스명 `PatchService` → `WindowsPatchService`, `IPatchService` 구현 명시. ② `enum PatchStatus`는 더 이상 이 파일에 없음 (`Patching` 네임스페이스의 `IPatchService.cs`에서 가져옴). ③ `MakeBackup`/`RestoreBackups`/`DeleteBackups`/`TryDelete`/`ExportConfig` 호출이 `PatchFileOps.*`로 변경. ④ `InstallDoorstop(GameInfo, string doorstopX64Path, string doorstopX86Path, string[] libraryPaths)` → `InstallDoorstop(GameInfo game, string[] ummLibraryPaths)`로 시그니처가 단순화되고, DLL 경로를 메서드 내부에서 `AppDomain.CurrentDomain.BaseDirectory`/`Doorstop/win/{x64,x86}/winhttp.dll` 기준으로 직접 계산. ⑤ `InstallAssembly`/`RemoveAssembly`의 `libraryPaths` 매개변수명이 `ummLibraryPaths`로 통일. 그 외 `GetPatchStatus`, `RemoveDoorstop`, `TryParseEntryPoint`, `ExtractAssemblyFileName`, `BuildStarterType`, `FindMethod`, `RemoveInjectedStarter`, `IsExecutable64Bit`의 본문은 원본과 동일하다.

- [ ] **Step 2: 빌드하여 컴파일 오류 없는지 확인**

Run: `dotnet build BetterUMM/BetterUMM.csproj -c Debug`
Expected: 빌드 성공 (단, 이 시점에는 `PatchService.cs`와 `WindowsPatchService.cs`가 공존하므로 `PatchStatus`/`UmmConfig` 같은 타입 이름이 두 네임스페이스에 중복 존재할 수 있다 — `BetterUMM.Services.PatchService`와 `BetterUMM.Services.Patching.WindowsPatchService`는 다른 네임스페이스이므로 충돌하지 않는다)

- [ ] **Step 3: 수동 회귀 확인을 위한 임시 단위 테스트 작성**

`BetterUMM.Tests/Services/Patching/WindowsPatchServiceTests.cs`:

```csharp
using BetterUMM.Models;
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class WindowsPatchServiceTests
    {
        [Fact]
        public void GetPatchStatus_ForNonExistentGame_ReturnsNotInstalled()
        {
            var service = new WindowsPatchService();
            var game = new GameInfo
            {
                Name = "Ghost",
                Path = "C:\\NonExistent\\Ghost.exe",
                GameDataPath = "C:\\NonExistent\\Ghost_Data",
                AssemblyName = "Assembly-CSharp.dll",
                PatchTarget = string.Empty,
                Folder = "Ghost",
                ModsDirectory = "Mods",
                ModInfo = "Info.json",
                GameExe = "Ghost.exe",
                EntryPoint = string.Empty,
                StartingPoint = string.Empty,
                UIStartingPoint = string.Empty,
                OldPatchTarget = string.Empty,
                GameVersionPoint = string.Empty,
                MinimalManagerVersion = string.Empty,
                HarmonyVersion = string.Empty
            };

            Assert.Equal(PatchStatus.NotInstalled, service.GetPatchStatus(game));
        }
    }
}
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter WindowsPatchServiceTests`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

(이 테스트는 Windows 호스트에서만 의미 있게 동작 — `GetPatchStatus`가 파일 존재 여부만 확인하므로 비-Windows에서도 통과하지만, 실제 Doorstop 설치/Assembly Injection 흐름의 실기 검증은 스펙에 따라 사용자가 별도로 수행한다.)

- [ ] **Step 5: 커밋**

```bash
git add BetterUMM/Services/Patching/WindowsPatchService.cs BetterUMM.Tests/Services/Patching/WindowsPatchServiceTests.cs
git commit -m "refactor: PatchService 로직을 WindowsPatchService로 이전하고 IPatchService 어댑팅"
```

---

### Task 8: `UnixDoorstopPatchService` (TDD — 래퍼 스크립트 생성 로직)

**Files:**
- Modify: `BetterUMM/Services/Patching/UnixDoorstopPatchService.cs`
- Test: `BetterUMM.Tests/Services/Patching/UnixDoorstopPatchServiceTests.cs`

- [ ] **Step 1: 실패하는 래퍼 스크립트 생성 테스트 작성**

`BetterUMM.Tests/Services/Patching/UnixDoorstopPatchServiceTests.cs`:

```csharp
using BetterUMM.Services.Patching;
using Xunit;

namespace BetterUMM.Tests.Services.Patching
{
    public class UnixDoorstopPatchServiceTests
    {
        [Fact]
        public void BuildWrapperScriptContent_ForLinuxLayout_GeneratesRelativeExecPath()
        {
            string content = UnixDoorstopPatchService.BuildWrapperScriptContent(
                targetDir: "/home/user/Games/MyGame",
                executablePath: "/home/user/Games/MyGame/MyGame",
                targetAssemblyPath: "/home/user/Games/MyGame/MyGame_Data/Managed/UnityModManager/UnityModManager.dll");

            Assert.Equal(
                "#!/bin/sh\n" +
                "exec \"$(dirname \"$0\")/run.sh\" \\\n" +
                "    \"./MyGame\" \\\n" +
                "    --doorstop-target-assembly \"/home/user/Games/MyGame/MyGame_Data/Managed/UnityModManager/UnityModManager.dll\"\n",
                content);
        }

        [Fact]
        public void BuildWrapperScriptContent_ForMacAppBundleLayout_UsesNestedRelativePath()
        {
            string content = UnixDoorstopPatchService.BuildWrapperScriptContent(
                targetDir: "/Users/foo/Games/MyGame.app",
                executablePath: "/Users/foo/Games/MyGame.app/Contents/MacOS/MyGame",
                targetAssemblyPath: "/Users/foo/Games/MyGame.app/Contents/Resources/Data/Managed/UnityModManager/UnityModManager.dll");

            Assert.Contains("\"./Contents/MacOS/MyGame\"", content);
            Assert.StartsWith("#!/bin/sh\n", content);
            Assert.Contains("--doorstop-target-assembly \"/Users/foo/Games/MyGame.app/Contents/Resources/Data/Managed/UnityModManager/UnityModManager.dll\"", content);
        }
    }
}
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter UnixDoorstopPatchServiceTests`
Expected: FAIL — `BuildWrapperScriptContent`가 `NotImplementedException`을 던지거나 컴파일 오류 (Task 5에서 만든 골격에는 해당 메서드가 없음)

- [ ] **Step 3: `UnixDoorstopPatchService` 전체 구현으로 교체**

`BetterUMM/Services/Patching/UnixDoorstopPatchService.cs`:

```csharp
using System;
using System.IO;
using BetterUMM.Models;
using BetterUMM.Services;

namespace BetterUMM.Services.Patching
{
    public class UnixDoorstopPatchService : IPatchService
    {
        private const string LibSoName = "libdoorstop.so";
        private const string LibDylibName = "libdoorstop.dylib";
        private const string OfficialRunScriptName = "run.sh";
        private const string WrapperScriptName = "run_umm.sh";
        private const string UmmSubDir = "UnityModManager";
        private const string UmmDllName = "UnityModManager.dll";

        private const UnixFileMode ExecutableMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        public PatchStatus GetPatchStatus(GameInfo game)
        {
            var (_, targetDir) = ResolvePaths(game);
            string libName = OperatingSystem.IsMacOS() ? LibDylibName : LibSoName;
            bool hasLib = File.Exists(Path.Combine(targetDir, libName));
            bool hasWrapper = File.Exists(Path.Combine(targetDir, WrapperScriptName));
            return hasLib && hasWrapper ? PatchStatus.Doorstop : PatchStatus.NotInstalled;
        }

        public bool InstallDoorstop(GameInfo game, string[] ummLibraryPaths)
        {
            var (executablePath, targetDir) = ResolvePaths(game);
            string managedPath = Path.Combine(game.GameDataPath, "Managed");
            string ummDir = Path.Combine(managedPath, UmmSubDir);
            string targetAssemblyPath = Path.Combine(ummDir, UmmDllName);
            string gameConfigPath = Path.Combine(ummDir, "Config.xml");

            try
            {
                if (!Directory.Exists(ummDir))
                    Directory.CreateDirectory(ummDir);

                string doorstopResourceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Doorstop");
                string libDestPath = CopyNativeLibrary(executablePath, doorstopResourceDir, targetDir);

                string runScriptDest = Path.Combine(targetDir, OfficialRunScriptName);
                File.Copy(Path.Combine(doorstopResourceDir, "unix", OfficialRunScriptName), runScriptDest, true);

                string wrapperDest = Path.Combine(targetDir, WrapperScriptName);
                File.WriteAllText(wrapperDest, BuildWrapperScriptContent(targetDir, executablePath, targetAssemblyPath));

                File.SetUnixFileMode(libDestPath, ExecutableMode);
                File.SetUnixFileMode(runScriptDest, ExecutableMode);
                File.SetUnixFileMode(wrapperDest, ExecutableMode);

                foreach (var lib in ummLibraryPaths)
                    File.Copy(lib, Path.Combine(ummDir, Path.GetFileName(lib)), true);

                PatchFileOps.ExportConfig(game, gameConfigPath);

                LoggerService.Log(
                    "Doorstop이 설치되었습니다. 패치된 상태로 게임을 실행하려면 게임 폴더의 'run_umm.sh'를 사용하세요. " +
                    "Steam: 라이브러리 > 속성 > 실행 옵션에 './run_umm.sh %command%' 입력 / " +
                    "비-Steam: 터미널에서 './run_umm.sh' 실행.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "UnixDoorstopPatchService.InstallDoorstop");
                return false;
            }
        }

        public bool RemoveDoorstop(GameInfo game)
        {
            var (_, targetDir) = ResolvePaths(game);
            string ummDir = Path.Combine(game.GameDataPath, "Managed", UmmSubDir);
            string libName = OperatingSystem.IsMacOS() ? LibDylibName : LibSoName;
            try
            {
                PatchFileOps.TryDelete(Path.Combine(targetDir, libName));
                PatchFileOps.TryDelete(Path.Combine(targetDir, OfficialRunScriptName));
                PatchFileOps.TryDelete(Path.Combine(targetDir, WrapperScriptName));
                if (Directory.Exists(ummDir))
                    Directory.Delete(ummDir, true);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "UnixDoorstopPatchService.RemoveDoorstop");
                return false;
            }
        }

        public bool InstallAssembly(GameInfo game, string[] ummLibraryPaths) =>
            throw new NotSupportedException("Assembly Injection 방식은 현재 Windows에서만 지원됩니다.");

        public bool RemoveAssembly(GameInfo game) =>
            throw new NotSupportedException("Assembly Injection 방식은 현재 Windows에서만 지원됩니다.");

        private static (string ExecutablePath, string TargetDir) ResolvePaths(GameInfo game)
        {
            string executablePath = OperatingSystem.IsMacOS()
                ? MacAppBundleHelper.ResolveExecutablePath(game.Path)
                : game.Path;
            string targetDir = OperatingSystem.IsMacOS() ? game.Path : Path.GetDirectoryName(executablePath)!;
            return (executablePath, targetDir);
        }

        private static string CopyNativeLibrary(string executablePath, string doorstopResourceDir, string targetDir)
        {
            string sourcePath;
            string destFileName;
            if (OperatingSystem.IsMacOS())
            {
                sourcePath = Path.Combine(doorstopResourceDir, "osx", LibDylibName);
                destFileName = LibDylibName;
            }
            else
            {
                bool? is64 = ElfBinaryInspector.Is64Bit(executablePath);
                string arch = is64 == false ? "x86" : "x64";
                sourcePath = Path.Combine(doorstopResourceDir, "linux", arch, LibSoName);
                destFileName = LibSoName;
            }
            string destPath = Path.Combine(targetDir, destFileName);
            File.Copy(sourcePath, destPath, true);
            return destPath;
        }

        public static string BuildWrapperScriptContent(string targetDir, string executablePath, string targetAssemblyPath)
        {
            string relativeExecutablePath = "./" + Path.GetRelativePath(targetDir, executablePath).Replace('\\', '/');
            return "#!/bin/sh\n" +
                $"exec \"$(dirname \"$0\")/{OfficialRunScriptName}\" \\\n" +
                $"    \"{relativeExecutablePath}\" \\\n" +
                $"    --doorstop-target-assembly \"{targetAssemblyPath}\"\n";
        }
    }
}
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj --filter UnixDoorstopPatchServiceTests`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 5: 전체 빌드로 회귀 확인**

Run: `dotnet build BetterUMM/BetterUMM.csproj -c Debug`
Expected: 빌드 성공

- [ ] **Step 6: 커밋**

```bash
git add BetterUMM/Services/Patching/UnixDoorstopPatchService.cs BetterUMM.Tests/Services/Patching/UnixDoorstopPatchServiceTests.cs
git commit -m "feat: Linux/macOS Doorstop 패치를 수행하는 UnixDoorstopPatchService 구현"
```

---

### Task 9: `MainViewModel` 연결

**Files:**
- Modify: `BetterUMM/ViewModels/MainViewModel.cs:13` (using 추가)
- Modify: `BetterUMM/ViewModels/MainViewModel.cs:23` (필드 타입/초기화)
- Modify: `BetterUMM/ViewModels/MainViewModel.cs:112-158` (`SelectGameAsync`)
- Modify: `BetterUMM/ViewModels/MainViewModel.cs:301-308` (`InstallDoorstop` 호출부)

- [ ] **Step 1: `using` 추가**

`BetterUMM/ViewModels/MainViewModel.cs:13` 근처의 기존:

```csharp
using BetterUMM.Services;
```

바로 아래 줄에 추가:

```csharp
using BetterUMM.Services.Patching;
```

- [ ] **Step 2: `_patchService` 필드 타입을 `IPatchService`로 변경**

`BetterUMM/ViewModels/MainViewModel.cs:23`의 기존:

```csharp
        private readonly PatchService _patchService = new();
```

다음으로 교체:

```csharp
        private readonly IPatchService _patchService = PatchServiceFactory.Create();
```

- [ ] **Step 3: `SelectGameAsync`를 OS별 분기 로직으로 전체 교체**

`BetterUMM/ViewModels/MainViewModel.cs:112-158`의 `SelectGameAsync` 메서드 전체를 다음으로 교체:

```csharp
        private async Task SelectGameAsync()
        {
            string path;
            string? appBundlePath = null;

            if (OperatingSystem.IsMacOS())
            {
                var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Game .app Bundle",
                    AllowMultiple = false
                });
                if (folders.Count == 0) return;
                appBundlePath = folders[0].Path.LocalPath;
                path = MacAppBundleHelper.ResolveExecutablePath(appBundlePath);
            }
            else
            {
                var fileTypeFilter = OperatingSystem.IsWindows()
                    ? new[] { new FilePickerFileType("Executable files") { Patterns = new[] { "*.exe" } }, FilePickerFileTypes.All }
                    : new[] { FilePickerFileTypes.All };

                var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Game Executable",
                    AllowMultiple = false,
                    FileTypeFilter = fileTypeFilter
                });
                if (files.Count == 0) return;
                path = files[0].Path.LocalPath;
            }

            string representativePath = appBundlePath ?? path;
            string folderName = Path.GetFileName(Path.GetDirectoryName(representativePath)) ?? "";
            string exeName = Path.GetFileName(representativePath);

            var config = _configService.GetGameConfig(folderName) ?? _configService.GetGameConfigByExe(exeName);

            string gameDataPath = appBundlePath != null
                ? Path.Combine(appBundlePath, "Contents", "Resources", "Data")
                : Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_Data");

            SelectedGame = new GameInfo
            {
                Name = config?.Name ?? Path.GetFileNameWithoutExtension(representativePath),
                Path = appBundlePath ?? path,
                GameDataPath = gameDataPath,
                AssemblyName = config != null ? (config.EntryPoint.Split('[', ']').Length > 2 ? config.EntryPoint.Split('[', ']')[1] : "Assembly-CSharp.dll") : "Assembly-CSharp.dll",
                PatchTarget = config?.EntryPoint ?? string.Empty,
                CurrentPatchMethod = PatchMethod.Doorstop,
                Folder = config?.Folder ?? folderName,
                ModsDirectory = config?.ModsDirectory ?? "Mods",
                ModInfo = config?.ModInfo ?? "Info.json",
                GameExe = config?.GameExe ?? exeName,
                EntryPoint = config?.EntryPoint ?? string.Empty,
                StartingPoint = config?.StartingPoint ?? string.Empty,
                UIStartingPoint = config?.UIStartingPoint ?? string.Empty,
                OldPatchTarget = config?.OldPatchTarget ?? string.Empty,
                GameVersionPoint = config?.GameVersionPoint ?? string.Empty,
                MinimalManagerVersion = config?.MinimalManagerVersion ?? string.Empty,
                HarmonyVersion = config?.HarmonyVersion ?? string.Empty
            };
        }
```

> **확인:** `OpenFolderPickerAsync`/`FolderPickerOpenOptions`는 Avalonia의 `IStorageProvider`에 포함된 API이며, 이미 사용 중인 `OpenFilePickerAsync`/`FilePickerOpenOptions`와 같은 `Avalonia.Platform.Storage` 네임스페이스에 있다 — 별도 using 추가가 필요 없다 (파일 상단에 이미 관련 using이 있는지 빌드 시 확인하고, 없다면 `using Avalonia.Platform.Storage;`를 추가한다).

- [ ] **Step 4: `InstallDoorstop` 호출부 변경**

`BetterUMM/ViewModels/MainViewModel.cs:301-308`의 기존:

```csharp
            if (SelectedGame.CurrentPatchMethod == PatchMethod.Doorstop)
            {
                ok = _patchService.InstallDoorstop(
                    SelectedGame,
                    Path.Combine(baseDir, "winhttp_x64.dll"),
                    Path.Combine(baseDir, "winhttp_x86.dll"),
                    libs);
            }
```

다음으로 교체:

```csharp
            if (SelectedGame.CurrentPatchMethod == PatchMethod.Doorstop)
            {
                ok = _patchService.InstallDoorstop(SelectedGame, libs);
            }
```

(`string baseDir = AppDomain.CurrentDomain.BaseDirectory;` 선언은 `ummSourceDir` 등 다른 곳에서 계속 사용되므로 그대로 둔다.)

- [ ] **Step 5: 빌드 확인**

Run: `dotnet build BetterUMM/BetterUMM.csproj -c Debug`
Expected: 빌드 성공, 경고 없음 (특히 `PatchService` 타입 미사용 경고가 뜨면 Task 10에서 정리됨을 인지)

- [ ] **Step 6: 앱 실행하여 게임 선택 → 패치 흐름 수동 확인 (Windows)**

Run: `dotnet run --project BetterUMM/BetterUMM.csproj`
Expected: 앱이 정상 실행되고, "Select Game Executable" 버튼 클릭 시 `.exe` 필터가 있는 파일 선택 대화상자가 뜨며, 게임 선택 후 패치 상태가 정상적으로 표시된다 (기존과 동일한 동작).

- [ ] **Step 7: 커밋**

```bash
git add BetterUMM/ViewModels/MainViewModel.cs
git commit -m "refactor: MainViewModel이 IPatchService를 사용하고 OS별 게임 선택 흐름을 지원하도록 변경"
```

---

### Task 10: 정리 — 기존 `PatchService.cs` 삭제 및 최종 검증

**Files:**
- Delete: `BetterUMM/Services/PatchService.cs`

- [ ] **Step 1: 더 이상 참조되지 않음을 확인**

Run: `grep -rn "Services.PatchService\|new PatchService(" BetterUMM/ --include="*.cs"`
Expected: 출력 없음 (단, `BetterUMM.Services.Patching.WindowsPatchService`/`UnixDoorstopPatchService`/`PatchServiceFactory`/`PatchStatus` 등 새 네임스페이스의 타입은 매칭되지 않아야 하므로 검색어가 정확히 `Services.PatchService`/`new PatchService(`인지 확인)

- [ ] **Step 2: 파일 삭제**

```bash
git rm BetterUMM/Services/PatchService.cs
```

- [ ] **Step 3: 전체 빌드**

Run: `dotnet build BetterUMM.slnx -c Debug`
Expected: 빌드 성공, 오류 0건

- [ ] **Step 4: 전체 테스트 스위트 실행**

Run: `dotnet test BetterUMM.Tests/BetterUMM.Tests.csproj`
Expected: 모든 테스트 통과 (`Failed: 0`)

- [ ] **Step 5: 최종 커밋**

```bash
git add -A
git commit -m "cleanup: 레거시 PatchService.cs 제거 (WindowsPatchService로 대체 완료)"
```

---

## Out of Scope (스펙에서 명시적으로 제외됨)

- macOS/Linux용 Assembly Injection 패치 방식 — 현재는 `NotSupportedException`을 던지는 것으로 충분
- 실기(real-hardware) 검증 — Linux/macOS 환경에서의 실제 Doorstop 설치/실행 테스트는 사용자가 별도로 수행
- `linux-x64`/`osx-x64`/`osx-arm64` 게시 프로필(`PublishProfiles`) 추가 — 별도 작업으로 분리
