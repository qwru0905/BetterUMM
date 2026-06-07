# BetterUMM Mac/Linux 포팅 설계

## 배경 및 목표

BetterUMM은 현재 WPF(`net8.0-windows`, `UseWPF`)로 작성된 Windows 전용 GUI다.
WPF는 Windows 전용 프레임워크이므로, Mac/Linux 지원을 위해서는 UI 레이어를 크로스플랫폼
프레임워크로 전면 이식하고, 패치 로직(`PatchService`)의 OS 종속 부분을 분기해야 한다.

**목표**: Windows/Linux/macOS(Intel + Apple Silicon) 모두에서 동작하는 단일 코드베이스.

**제약**: 개발 환경이 Windows뿐이므로, Linux/macOS 동작은 코드/빌드 레벨까지만 검증
가능하고 실기 테스트는 사용자가 직접 수행한다.

## 아키텍처 개요

기존 MVVM 구조(`MainViewModel`, `RelayCommand`, `INotifyPropertyChanged`)는
프레임워크 비종속 BCL 타입으로 구성되어 있어 그대로 유지한다. 변경은 세 레이어에
집중된다.

1. **UI 레이어**: WPF → **Avalonia UI**로 전면 교체 (WPF 코드 제거)
2. **서비스 레이어**: `PatchService`의 Doorstop 설치/제거 로직을 OS별로 분기.
   Assembly Injection 방식(Mono.Cecil IL 조작)은 플랫폼 독립적이라 거의 그대로 유지
3. **리소스/빌드**: 플랫폼별 Doorstop 바이너리를 리소스에 추가하고,
   `linux-x64`/`osx-x64`/`osx-arm64` 퍼블리시 프로파일을 추가

`csproj`는 `net8.0-windows` + `UseWPF` → `net8.0` + Avalonia 패키지 참조로 변경한다.

## 1. UI 레이어 (WPF → Avalonia)

| WPF 요소 | Avalonia 대체 |
|---|---|
| `App`/`App.xaml` | `Avalonia.Application` + `AppBuilder` 부트스트랩 (`Program.cs` 진입점 신설) |
| `MainWindow`/XAML | Avalonia XAML로 재작성. `DataGrid`는 `Avalonia.Controls.DataGrid` 패키지, 스타일/트리거 문법은 Avalonia 식으로 변환 |
| `Converters/ValueConverters.cs` (`System.Windows.Data.IValueConverter`) | `Avalonia.Data.Converters.IValueConverter`로 포팅 |
| `Microsoft.Win32.OpenFileDialog` | `TopLevel.StorageProvider`의 `OpenFilePickerAsync`/`OpenFolderPickerAsync` (비동기 API이므로 `SelectGame`/`InstallMod` 호출부를 async로 변경) |
| `System.Windows.MessageBox` | **`MsBox.Avalonia`** 패키지 추가. WPF MessageBox와 유사한 API라 변경 범위 최소 |
| `LoggerService`의 `using System.Windows;` | 미사용 import로 확인됨 — 제거 |

`MainViewModel`, `RelayCommand`, `ConfigService`, `ModService`, `ProfileService`,
`AppSettingsService`는 WPF 의존이 없으므로 변경 없이 유지하고, `MessageBox`/
`OpenFileDialog` 호출부만 교체한다.

## 2. PatchService — Doorstop 플랫폼 분기

NeighTools/UnityDoorstop v4.5.0의 공식 `run.sh`와 릴리즈 자산 구조를 직접 확인했다
(`https://github.com/NeighTools/UnityDoorstop`).

### 플랫폼별 동작 방식

- **Windows** (기존 유지): `winhttp.dll` 프록시가 게임 실행 시 자동 로드됨 — 투명하게 동작
- **Linux**: `libdoorstop.so`(x64/x86) + `run.sh`를 게임 폴더에 배치. `run.sh`가
  `LD_PRELOAD`/`LD_LIBRARY_PATH`/`DOORSTOP_*` 환경변수를 설정한 뒤 원본 실행 파일을
  `exec`. 설정값은 INI가 아니라 **`run.sh` 상단의 쉘 변수**(`executable_name`,
  `target_assembly` 등)로 주입
- **macOS**: `libdoorstop.dylib`(universal — x64/arm64 모두 커버, 별도 아키텍처
  분기 불필요) + `run.sh`. 스크립트가 `.app` 번들 내부의
  `Contents/MacOS/<실제 실행파일>`을 자동 탐지하고, Apple Silicon에서는
  `arch -e DYLD_INSERT_LIBRARIES=...`로 네이티브 실행을 강제

**중요한 차이점**: Windows와 달리 Linux/macOS에서는 게임을 원본 실행 파일이 아닌
`run.sh`를 통해 실행해야 Doorstop이 동작한다(투명하지 않음). Steam 게임이라면
실행 옵션에 `./run.sh %command%` 등록이 필요할 수 있다. 이는 설치 완료 후 안내
다이얼로그/툴팁으로 사용자에게 노출한다 (별도 자동화는 시도하지 않음 — 원본 실행
파일 백업/이름 변경 등은 Steam 무결성 검사와 충돌하거나 다른 도구와 충돌할 위험이
있어 범위에서 제외).

### 코드 변경

- `GetPatchStatus`: Linux/Mac에서는 게임 폴더의 `libdoorstop.{so,dylib}` + `run.sh`
  존재 여부로 Doorstop 설치 상태 판별
- `InstallDoorstop`/`RemoveDoorstop`: `RuntimeInformation.IsOSPlatform`으로 분기.
  Linux/Mac 전용 설치 메서드를 추가해 `run.sh` 템플릿의 `executable_name`/
  `target_assembly` 등을 게임에 맞게 채워 배치
- `IsExecutable64Bit` (PE 파서): Windows 전용으로 유지하고, **ELF 헤더 파서를 신규
  추가**해 Linux용 `libdoorstop.so`의 x64/x86 중 올바른 것을 선택. macOS는 universal
  바이너리이므로 아키텍처 판별 불필요
- **Assembly Injection 방식은 거의 그대로 유지** — Mono.Cecil IL 조작은 플랫폼
  독립적. `ExtractAssemblyFileName`/`managedPath` 등 경로 로직에 macOS `.app` 번들
  구조(`Contents/Resources/Data/Managed/...`) 대응만 추가

## 3. 게임 탐색 / `GameInfo` — 플랫폼별 차이

- **파일 선택 필터**: 현재 `*.exe` 고정 → 플랫폼 분기 필요
  - Windows: `*.exe`
  - Linux: 확장자 없는 실행 파일 (확장자 필터 대신 "모든 파일" 노출 + 실행 권한 체크)
  - macOS: `.app` 번들 디렉터리 선택 (Avalonia 폴더 피커)
- **`GameDataPath` 도출**:
  - Windows/Linux: `<ExeName>_Data` (기존 로직 유지)
  - macOS: `<App>.app/Contents/Resources/Data` — `.app` 선택 시 내부 구조를 따라가는
    별도 로직 필요
- **`IsExecutable64Bit`**: 위 2절 참고 (Windows=PE, Linux=ELF, macOS=불필요)

## 4. 리소스 & 빌드

### 리소스 추가 (`Resources/Doorstop/`)

NeighTools/UnityDoorstop v4.5.0 공식 릴리즈에서 다운로드해 추가 (구조 확인 완료):

```
Resources/Doorstop/
├── linux/
│   ├── x64/libdoorstop.so
│   ├── x86/libdoorstop.so
│   └── run.sh
└── macos/
    ├── universal/libdoorstop.dylib
    └── run.sh
```

기존 `Resources/winhttp_x64.dll`, `winhttp_x86.dll`(Windows용)은 그대로 유지.

### `csproj` 변경

- `<TargetFramework>net8.0-windows</TargetFramework>` → `<TargetFramework>net8.0</TargetFramework>`
- `<UseWPF>true</UseWPF>` 제거
- 패키지 추가: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
  `Avalonia.Controls.DataGrid`, `MsBox.Avalonia`

### 퍼블리시 프로파일 (`Properties/PublishProfiles/`)

기존 `win-x64.pubxml` 외에 추가:
- `linux-x64.pubxml`
- `osx-x64.pubxml`
- `osx-arm64.pubxml`

(self-contained, single-file — 기존 win-x64 프로파일과 동일한 패턴)

## 5. 검증 방식

개발 환경이 Windows뿐이므로 검증은 다음 두 단계로 제한된다:

- **빌드 레벨**: 각 RID(`linux-x64`, `osx-x64`, `osx-arm64`, `win-x64`)로
  `dotnet publish` 성공 여부, 플랫폼 조건부 코드의 컴파일 정상 여부 확인
- **로직 레벨**: `RuntimeInformation.IsOSPlatform` 분기, 경로 조합, ELF/Mach-O
  헤더 파싱 등은 코드 리뷰 + 가능한 범위에서 단위 테스트로 검증 (예: 샘플 ELF
  헤더 바이트 배열로 파서 테스트)
- **실기 테스트**: 설치 후 실제 게임 동작 확인, `run.sh` 실행 등은 **사용자가
  직접 검증**해야 함을 명시

## 6. 단계적 진행 순서

각 단계는 별도 PR로 분리한다.

1. **UI 포팅**: WPF → Avalonia 전환. Windows에서 빌드/실행하며 검증 가능
2. **PatchService 플랫폼 분기**: Doorstop 설치/제거 로직 + 리소스 추가
3. **GameInfo/게임 탐색 플랫폼 분기**: 파일 피커, 경로 도출 로직
4. **퍼블리시 프로파일 추가**: 크로스 컴파일 검증 (`dotnet publish` 성공 확인)
