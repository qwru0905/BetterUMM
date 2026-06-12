# UMM Harmony 패치 에러 출처 표시 기능 설계

## 배경 / 목표

UnityModManager(UMM)에서 모드들이 Harmony로 게임 메소드를 패치하면, 그 메소드에서 예외가 발생했을 때
스택 트레이스에는 Harmony가 생성한 동적 메소드(`DMD<Type::Method>_xxxxxxxx` 등) 또는 모드 자신의
Prefix/Postfix 델리게이트가 나타날 뿐, "이 메소드에 어떤 모드가 패치를 걸었는가"는 직접적으로 드러나지
않는다. 여러 모드를 동시에 사용할 때 어떤 모드의 패치가 에러의 원인인지 파악하기 어렵다.

이 기능은 예외 로그가 출력될 때 해당 메소드에 패치를 건 모드 목록(Prefix/Postfix/Transpiler/Finalizer별
owner)을 자동으로 함께 출력하여, 디버깅 시 원인 모드를 빠르게 좁힐 수 있도록 한다.

## 적용 범위

- 대상: `UnityModManager/UnityModManager/` 런타임 코드 (UMM 포크)
- BetterUMM 설치기(`BetterUMM/`) 코드 변경 없음. 빌드 산출물인
  `BetterUMM/Resources/UnityModManager/UnityModManager.dll`만 갱신됨
- 적용되는 예외 경로 2가지:
  1. 모드가 직접 `catch`해서 `Logger.LogException` / `ModLogger.LogException`을 호출하는 경우
  2. Unity가 MonoBehaviour 콜백 등에서 잡지 못한(uncaught) 예외를 `Application.logMessageReceived`의
     `LogType.Exception`으로 보고하는 경우

## 아키텍처

### 신규 컴포넌트: `HarmonyDiagnostics.cs`

`UnityModManager/UnityModManager/`에 새 파일을 추가하고, `partial class UnityModManager` 내부에
`internal static class HarmonyDiagnostics`를 정의한다.

핵심 함수: `internal static string DescribePatches(MethodBase method)`

주어진 메소드와 관련된 Harmony 패치 정보를 사람이 읽을 수 있는 한 줄 문자열로 반환한다. 관련 패치가
없으면 빈 문자열을 반환한다.

세 가지 경우를 순서대로 시도한다:

- **Case A — `method`가 패치 대상(target) 메소드인 경우**
  `HarmonyLib.Harmony.GetPatchInfo(method)`로 직접 조회한다. `Prefixes`, `Postfixes`,
  `Transpilers`, `Finalizers` 각각의 `owner`를 모아 패치 종류별로 그룹화한다.

- **Case B — `method`가 Harmony/MonoMod가 생성한 동적 메소드인 경우**
  메소드 이름이 `DMD<Namespace.Type::MethodName>_xxxxxxxx` 형태의 정규식과 매치되면, 캡처한
  타입명/메소드명으로 `AppDomain.CurrentDomain.GetAssemblies()`를 순회해 원본 `MethodBase`를 찾고
  Case A로 처리한다. 매칭되지 않으면 다음 케이스로 넘어간다.

- **Case C — `method` 자체가 어떤 모드의 Prefix/Postfix/Transpiler/Finalizer 델리게이트인 경우**
  (예외가 패치 코드 내부에서 발생한 경우) `Harmony.GetAllPatchedMethods()`를 순회하며 각
  `PatchInfo`의 모든 패치 항목의 `PatchMethod`가 `method`와 일치하는지 확인한다. 일치하면
  "이 메소드는 `<owner>`가 등록한 `<Prefix/Postfix/...>`이며 대상은 `<원본 메소드>`" 형태로 보고한다.

세 경우 모두 매칭되지 않으면 빈 문자열을 반환한다.

### Owner ID → 모드 이름 매핑

Harmony owner ID(보통 `new Harmony(Info.Id)` 관례로 모드 ID와 동일)를
`UnityModManager.modEntries.FirstOrDefault(m => m.Info.Id == ownerId)?.Info.DisplayName`으로
조회한다. 매칭되는 모드가 없으면 owner ID 원문을 그대로 표시한다.

## 후크 지점 & 출력 형식

### A. `Logger.LogException` / `ModLogger.LogException` (`ModManager.cs`)

`Exception` 객체가 있으므로 가장 정확하다. 기존 로그 출력 이후, 예외의 첫 스택 프레임의
`MethodBase`로 `DescribePatches`를 호출하고, 결과가 있으면 추가 줄을 출력한다.

```csharp
try
{
    var frame = new StackTrace(e, false).GetFrame(0)?.GetMethod();
    var info = HarmonyDiagnostics.DescribePatches(frame);
    if (!string.IsNullOrEmpty(info))
        Write($"{prefix}  ↳ {info}");
}
catch { /* 진단 실패는 무시 - 원래 로그에 영향 없음 */ }
```

### B. `Application.logMessageReceived` (신규 구독, `UnityModManager.cs`)

`LogType.Exception`만 구독한다. `stackTrace` 문자열의 첫 줄을 정규식으로 파싱해
`Namespace.Type.Method` 형태를 추출하고, reflection으로 `MethodBase`를 찾아 동일하게
`DescribePatches`를 호출, 결과를 `UnityModManager.Logger.Log`로 출력한다.

### 출력 예시

```
[Manager] [Exception] NullReferenceException - Object reference not set to an instance of an object.
[Manager]   ↳ Harmony patches on PlayerController.TakeDamage: ExampleMod (Prefix), OtherMod (Postfix)
```

## 에러 처리 / 제한사항

- 모든 진단 로직(reflection, 정규식 매칭, Harmony API 호출)은 try-catch로 감싸 진단 자체의 실패가
  원래 로그 출력이나 게임 동작에 영향을 주지 않도록 한다.
- IL2CPP 빌드에서는 스택 트레이스가 네이티브 주소 기반일 수 있어 경로 B(문자열 파싱)가 동작하지
  않을 수 있다. 이 경우 매칭 실패로 처리되어 빈 문자열을 반환하며 추가 로그 없이 조용히 넘어간다.

## 검증 방법

- 단위 테스트: `BetterUMM.Tests`(net8.0)는 UnityModManager(netstandard2.1, UnityEngine 의존)를
  직접 참조하기 어려워 이번 범위에서는 별도 테스트 프로젝트를 만들지 않는다.
- 수동 검증: Harmony Prefix/Postfix에서 의도적으로 예외를 던지는 테스트 모드를 만들어 실제 게임에서
  `Log.txt`/콘솔에 "Harmony patches on X: ModName (Prefix)" 형태의 줄이 정상 출력되는지 확인한다.

## 빌드/배포

기존 `UnityModManager.csproj`의 `CopyToInstallerResources` post-build target을 통해 빌드 시
자동으로 `BetterUMM/Resources/UnityModManager/UnityModManager.dll`이 갱신된다. 추가 빌드 설정
변경은 필요 없다.
