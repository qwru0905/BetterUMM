# PatchService 멀티 OS 포팅 설계

## 배경 및 목표

[[2026-06-07-cross-platform-port-design.md]]에서 UI 레이어(WPF → Avalonia) 포팅을
완료했다 (`feature/cross-platform-port` 브랜치, 커밋 `48c2c72`~`d1ac929`). 이번
설계는 그 계획의 2단계인 **`PatchService`의 멀티 OS 대응**을 구체화한다.

`PatchService`는 현재 전적으로 Windows 전용이다:
- Doorstop 설치는 `winhttp.dll` 프록시 DLL을 게임 폴더에 배치하는 Windows DLL
  탐색 순서 하이재킹 기법에 의존한다.
- `IsExecutable64Bit`는 PE(`MZ`/`PE\0\0`) 헤더만 파싱한다.
- 게임 실행 파일이 `.exe`라고 가정한다 — macOS에서는 `.app` 번들, Linux에서는
  확장자 없는 ELF 바이너리다.

**목표**: Windows/Linux/macOS 모두에서 Doorstop 패치 설치·제거·상태 확인이
동작하는 `PatchService`. Assembly Injection 방식은 이번 작업 범위에서 제외하고
Windows에서만 계속 지원한다 (아래 "범위 제외" 참고).

**제약**: 개발 환경은 Windows뿐이다. Linux/macOS 네이티브 라이브러리·런처
스크립트가 실제 게임에서 동작하는지는 이 작업으로 검증할 수 없다 — 순수 로직
(바이너리 포맷 판별, 경로 해석, 스크립트 생성)만 단위 테스트로 검증하고, 실기
검증은 사용자가 직접 수행한다.

NeighTools/UnityDoorstop v4.5.0의 공식 릴리즈 자산과 `run.sh` 스크립트 전문을
직접 받아 확인했다 (`gh release view --repo NeighTools/UnityDoorstop`). 아래
설계는 그 실제 구조에 기반한다.

## 아키텍처

현재의 단일 `PatchService` 클래스를 인터페이스 + OS별 구현체로 분리하고,
팩토리가 시작 시점에 적절한 구현을 선택한다.

```
IPatchService
├── GetPatchStatus(GameInfo) : PatchStatus
├── InstallDoorstop(GameInfo, ...) : bool
├── RemoveDoorstop(GameInfo) : bool
├── InstallAssembly(GameInfo, ...) : bool   (Windows 전용, 그 외 NotSupportedException)
└── RemoveAssembly(GameInfo) : bool          (Windows 전용, 그 외 NotSupportedException)

PatchServiceFactory.Create()
  → OperatingSystem.IsWindows() / IsLinux() / IsMacOS() 로 분기

├── WindowsPatchService     — 기존 로직을 그대로 이동 (winhttp.dll 프록시 + PE 판별 + Assembly Injection)
└── UnixDoorstopPatchService — 신규: Linux + macOS 공용 (Doorstop 설치/제거/상태만, Assembly Injection은 미지원)
```

Linux와 macOS를 하나의 클래스로 묶는 이유: 공식 Doorstop 배포 자체가 `uname`으로
OS를 감지하는 **단일 `run.sh`**를 Linux/macOS 공용으로 제공한다. 두 플랫폼이
이미 같은 메커니즘(래퍼 스크립트 + `LD_PRELOAD`/`DYLD_INSERT_LIBRARIES`)을
공유하므로, 별도 클래스로 나누면 거의 동일한 로직이 중복될 뿐이다. 두 플랫폼이
실제로 다른 지점(네이티브 라이브러리 선택, 실행 파일 경로 해석)만 내부에서
분기한다.

순수 파일 유틸리티(`MakeBackup`/`RestoreBackups`/`DeleteBackups`/`ExportConfig`/
`UmmConfig`)는 OS 의존이 없으므로 `PatchFileOps` 같은 공유 내부 헬퍼로 추출해
두 구현체가 재사용한다 — 코드 중복 방지.

## 컴포넌트 상세

### `IPatchService` / `PatchServiceFactory`

`MainViewModel`은 현재 `PatchService`를 직접 `new`해서 사용한다
(`MainViewModel.cs:24`). 이를 `IPatchService _patchService = PatchServiceFactory.Create();`로
교체한다. 인터페이스 시그니처는 기존 `PatchService`의 public 메서드와 동일하게
유지해 `MainViewModel` 호출부 변경을 최소화한다.

### `WindowsPatchService`

기존 `PatchService.cs`의 내용을 거의 그대로 이동한다. `MakeBackup` 등 공유
유틸리티만 `PatchFileOps`로 옮기고 호출부를 그에 맞게 조정한다. 동작 변경 없음.

### `UnixDoorstopPatchService`

Linux와 macOS의 Doorstop 설치/제거/상태 확인을 담당한다.

**`GetPatchStatus`**: 게임 실행 파일과 같은 디렉터리(또는 macOS는 `.app` 번들
루트)에 `libdoorstop.{so,dylib}`와 생성된 래퍼 스크립트(`run_umm.sh`)가 모두
존재하면 `PatchStatus.Doorstop`, 아니면 `NotInstalled`.

**`InstallDoorstop`**:
1. `OperatingSystem.IsLinux()`/`IsMacOS()`로 분기해 실행 파일의 실제 경로를 해석
   - Linux: `game.Path` 그대로 사용
   - macOS: `MacAppBundleHelper.ResolveExecutablePath(game.Path)`로
     `.app/Contents/MacOS/<CFBundleExecutable>` 해석
2. 네이티브 라이브러리 선택 및 복사
   - Linux: `ElfBinaryInspector`로 아키텍처 판별 → `linux/x64/libdoorstop.so`
     또는 `linux/x86/libdoorstop.so`를 게임 폴더에 복사
   - macOS: 아키텍처 판별 없이 `osx/libdoorstop.dylib`(universal)을 `.app` 번들
     루트에 복사
3. 번들링된 공식 `unix/run.sh`를 게임 폴더에 복사 (수정 없이 그대로)
4. `run_umm.sh` 래퍼 스크립트를 생성 — 공식 `run.sh`를 올바른 인자로 호출만 함:
   ```sh
   #!/bin/sh
   exec "$(dirname "$0")/run.sh" \
        "<실행 파일 상대경로>" \
        --doorstop-target-assembly "<UnityModManager.dll 경로>"
   ```
5. `File.SetUnixFileMode`로 `run.sh`/`run_umm.sh`/복사된 라이브러리에 실행 권한 부여
   (Windows에서는 호출되지 않으므로 플랫폼 제약 문제 없음)
6. `ExportConfig`로 `Config.xml` 작성 (기존 `PatchFileOps` 재사용)
7. 설치 완료 후 사용자에게 실행 방법 안내 — `MainViewModel`에 안내 메시지를
   반환하거나 `LoggerService`로 기록하고, UI에서 다이얼로그로 노출
   (예: "Steam 라이브러리 > 속성 > 실행 옵션에 `./run_umm.sh %command%`를
   입력하세요. 비-Steam 게임은 터미널에서 `./run_umm.sh` 실행")

원본 게임 실행 파일은 전혀 건드리지 않는다 — 기존 Windows Doorstop 선택의
근거였던 "Steam 무결성 검사 통과" 속성을 그대로 유지한다.

**`RemoveDoorstop`**: 복사된 `libdoorstop.{so,dylib}`, `run.sh`, `run_umm.sh`,
`Config.xml`, UMM 디렉터리를 삭제. 원본 파일은 처음부터 손대지 않았으므로 복원
로직이 필요 없다 (Windows의 백업/복원과 다른 점).

**`InstallAssembly`/`RemoveAssembly`**: `throw new NotSupportedException("Assembly Injection 방식은 현재 Windows에서만 지원됩니다.")`

### `ElfBinaryInspector`

기존 `PatchService.IsExecutable64Bit`(PE 파서)와 동일한 패턴으로, ELF 매직
넘버를 파싱한다.

```csharp
public static bool? Is64Bit(string filePath)
{
    // 매직: 0x7F 'E' 'L' 'F' (offset 0)
    // EI_CLASS 바이트 (offset 4): 1 = ELFCLASS32, 2 = ELFCLASS64
}
```

### `MacAppBundleHelper`

`.app` 번들 경로를 받아 `Contents/Info.plist`를 파싱하고
`CFBundleExecutable` 키 값을 읽어 실제 실행 파일 경로
(`<App>.app/Contents/MacOS/<값>`)를 반환한다. macOS의 `Info.plist`는 XML
또는 binary plist 포맷일 수 있으므로, `System.Xml`로 우선 파싱을 시도하고
실패 시 binary plist 파싱이 필요 — .NET BCL에 binary plist 파서가 없으므로
간단한 binary plist 리더(또는 `plutil` 외부 호출)가 필요할 수 있다. 다만 Unity가
생성하는 `Info.plist`는 통상 XML 포맷이므로, 1차 구현은 XML 파싱만 지원하고
binary plist는 `NotSupportedException`으로 명시한다 (실기 검증 단계에서 필요시
추가).

### `PatchFileOps` (공유 내부 헬퍼)

기존 `PatchService`에서 `MakeBackup`/`RestoreBackups`/`DeleteBackups`/
`TryDelete`/`ExportConfig`/`UmmConfig` 클래스를 추출해 `internal static`
헬퍼로 옮긴다. 순수 파일 I/O와 XML 직렬화이므로 OS 의존이 전혀 없다.

## 리소스 레이아웃 변경

기존 `Resources/winhttp_x64.dll`, `Resources/winhttp_x86.dll`을
`Resources/Doorstop/` 하위로 재배치하고 Linux/macOS 자산을 추가한다:

```
BetterUMM/Resources/Doorstop/
├── win/
│   ├── x64/winhttp.dll
│   └── x86/winhttp.dll
├── linux/
│   ├── x64/libdoorstop.so
│   └── x86/libdoorstop.so
├── osx/
│   └── libdoorstop.dylib        (universal — x64 + arm64 fat binary)
└── unix/
    └── run.sh                    (NeighTools/UnityDoorstop v4.5.0 공식, 무수정)
```

모든 자산은 NeighTools/UnityDoorstop v4.5.0 공식 릴리즈에서 직접 받아 추가한다
(라이선스 확인 후 커밋). `BetterUMM.csproj`의 `<None Include="Resources\...">`
항목을 새 경로에 맞게 갱신하고, `CopyToOutputDirectory`로 빌드 출력에 포함되도록
설정한다 (기존 `winhttp_x64.dll` 패턴과 동일).

## `MainViewModel` / 파일 피커 조정

`SelectGameAsync`(`MainViewModel.cs:107`)의 `FilePickerOpenOptions`가 현재
`*.exe` 필터로 고정되어 있다. OS별로 분기한다:

- **Windows**: 기존 `*.exe` 필터 유지
- **Linux**: 확장자 필터 대신 `FilePickerFileTypes.All` — 실행 파일에 보통
  확장자가 없으므로 패턴 매칭이 의미 없음
- **macOS**: `OpenFolderPickerAsync`로 `.app` 번들(디렉터리) 선택을 허용

선택된 경로가 `.app` 번들이면 `MacAppBundleHelper.ResolveExecutablePath`로
실제 실행 파일 경로를 구해 `GameInfo.Path`/`GameExe` 등을 채운다 — 이렇게 하면
`PatchService` 쪽에서도 동일한 헬퍼를 통해 일관된 경로를 얻으므로 중복 해석
로직이 생기지 않는다.

`GameDataPath` 계산도 분기가 필요하다. 현재 `MainViewModel.SelectGameAsync`
(`MainViewModel.cs:139`)는 다음과 같이 고정 규칙으로 도출한다:

```csharp
GameDataPath = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_Data")
```

이는 Unity의 `<ExeName>_Data` 표준 레이아웃(Windows/Linux 공통)에는 맞지만,
macOS `.app` 번들 내부는 `<App>.app/Contents/Resources/Data` 구조다. 선택된
경로가 `.app` 번들일 때는 다음으로 분기한다:

```csharp
GameDataPath = OperatingSystem.IsMacOS() && Directory.Exists(appBundlePath)
    ? Path.Combine(appBundlePath, "Contents", "Resources", "Data")
    : Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_Data");
```

## 에러 처리

- 지원하지 않는 OS(예: FreeBSD)에서는 `PatchServiceFactory.Create()`가
  `PlatformNotSupportedException`을 던진다 — 공식 `run.sh`도 동일하게
  명시적으로 거부한다.
- `MacAppBundleHelper`가 `Info.plist`를 찾지 못하거나 `CFBundleExecutable`
  키가 없으면 `FileNotFoundException`/`InvalidDataException`을 던지고
  `LoggerService.LogException`으로 기록 — 기존 `PatchService`의
  catch-log-return-false 패턴을 따른다.
- `ElfBinaryInspector.Is64Bit`가 매직 넘버를 인식하지 못하면 `null` 반환
  (기존 `IsExecutable64Bit`와 동일한 계약 — 호출부에서 기본값으로 폴백).

## 테스트 전략

현재 저장소에는 테스트 프로젝트가 없다. xUnit 기반 테스트 프로젝트
(`BetterUMM.Tests`)를 신설해 **순수 로직만** 검증한다:

| 테스트 대상 | 방법 |
|---|---|
| `ElfBinaryInspector.Is64Bit` | 샘플 ELF32/ELF64 헤더 바이트 배열(첫 20바이트 정도)을 임시 파일로 써서 판별 |
| `MachOBinaryInspector`(필요시) | Mach-O 32/64/Fat 매직 바이트 배열로 판별 — 실제로는 universal dylib만 쓰므로 우선순위 낮음 |
| `MacAppBundleHelper.ResolveExecutablePath` | XML `Info.plist` 샘플 문자열 파싱 → 경로 조합 검증 |
| `run_umm.sh` 생성 내용 | 문자열 조립 결과가 기대하는 셸 스크립트 텍스트와 일치하는지 |
| 리소스 레이아웃 | `Resources/Doorstop/` 하위 기대 파일들이 빌드 출력에 존재하는지 |
| `PatchFileOps` (백업/복원/Config 직렬화) | 임시 디렉터리에서 round-trip 검증 — 기존 로직, 플랫폼 무관 |

다음은 **이 작업으로 검증 불가능** — 계획 문서에 "TODO: 실기 검증 필요"로
명시하고 사용자가 추후 직접 확인한다:
- 실제 Doorstop 주입이 게임을 패치된 상태로 구동시키는지
- `LD_PRELOAD`/`DYLD_INSERT_LIBRARIES` 환경변수 기반 실행이 실제 게임/Steam
  환경에서 성공하는지
- Apple Silicon에서 `arch -e` 분기가 정상 동작하는지
- Steam 무결성 검사와 충돌 없이 동작하는지

## 범위 제외 (이번 작업에서 다루지 않음)

- **Mac/Linux Assembly Injection 패치**: Mono.Cecil IL 조작 자체는 플랫폼
  독립적이지만, macOS `.app` 번들 내부의 `Managed` 폴더 경로 탐색 및 실기
  검증이 필요한 별도 작업이다. 이번에는 `NotSupportedException`으로 명시하고
  추후 단계로 분리한다 (사용자 확인: "둘 다 구현을 목표로 잡고 있으나, 일단
  Doorstop을 먼저 만드는 것으로 하자").
- **실기(Mac/Linux 하드웨어/VM) 검증**: 개발 환경 제약으로 이 작업 범위에서는
  불가능. 사용자가 추후 직접 검증.
- **`linux-x64`/`osx-x64`/`osx-arm64` 퍼블리시 프로파일 추가**: 원래 계획의
  4단계 항목이며, 이번 PatchService 작업과는 독립적이므로 별도로 진행한다.
