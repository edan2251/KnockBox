# 프로젝트 폴더 안내

팀원의 게임·UI·맵 작업은 **Assets/_Project**에서 시작합니다. `Assets/Scenes`와 `Assets/Settings`는 이름이 일반적이지만 역할이 다릅니다. 특히 **Settings는 현재 게임이 참조하므로 통째로 삭제하면 안 됩니다.**

2026-11-13에 씬·설정 참조를 재확인했습니다. 작업 씬 3개와 Settings의 렌더링·입력 에셋은 유지합니다. 게임에 쓰지 않는 Lit 2D 템플릿과 Fusion 실습 씬은 백업 후 정리했습니다. 현재 작업 상태는 [진행 현황](../ai_docs/progress/PROGRESS.md), 복원 자료는 [보관 안내](../ai_docs/HISTORY.md)를 참고하세요.

## 자주 여는 위치


[Assets/_Project/Scenes](../../Assets/_Project/Scenes/)

쓰임새: 실제 게임과 맵 작업·검증 씬

처음 볼 곳: Main: 전체 게임 흐름, MapAuthoring: 맵 편집, Sandbox: 검증·재현.


[Assets/_Project/Prefabs](../../Assets/_Project/Prefabs/)

쓰임새: 화면과 게임 오브젝트의 재사용 구성

처음 볼 곳: UI 편집은 [팀 안내](TEAM_WORKFLOW.md)를 따라 실제 Main 연결을 확인합니다.


[Assets/_Project/Art](../../Assets/_Project/Art/), [Audio](../../Assets/_Project/Audio/)

쓰임새: 이미지·재질·소리

처음 볼 곳: 추가·교체와 공용 도형 주의점은 [아트 안내](ART_WORKFLOW.md)를 봅니다.


[Assets/_Project/Data](../../Assets/_Project/Data/)

쓰임새: 맵 정의와 설정 에셋

처음 볼 곳: Maps의 정의와 GameMaps 목록을 확인합니다.


[Assets/_Project/Resources](../../Assets/_Project/Resources/)

쓰임새: 코드가 이름으로 불러오는 테마·폰트·셰이더

처음 볼 곳: 씬에 직접 연결되지 않아도 쓰일 수 있어 경로를 임의로 바꾸지 않습니다.


[Assets/_Project/Scripts](../../Assets/_Project/Scripts/)

쓰임새: 게임 진행·표시·판정 코드

처음 볼 곳: [아키텍처](../ai_docs/HISTORY.md)와 [온라인 코드 지도](../ai_docs/ONLINE_MULTIPLAYER.md#코드-지도)를 참고합니다.


[Assets/_Project/Editor](../../Assets/_Project/Editor/), [Tests](../../Assets/_Project/Tests/)

쓰임새: Unity 편의 도구와 자동 검사

처음 볼 곳: 게임 실행 코드와 구분해서 관리합니다.


[Packages](../../Packages/), [ProjectSettings](../../ProjectSettings/)

쓰임새: 패키지·Unity 버전·입력·렌더링 설정

처음 볼 곳: 에셋과 함께 커밋합니다.


[Tools](../../Tools/), [docs](../README.md)

쓰임새: 실행·검증 도구와 사람용 문서

처음 볼 곳: 도구 현장 안내는 Tools, 팀 전체 안내는 docs에 둡니다.


`LocalOnly`는 개인 자료용 제외 폴더입니다. 존재한다고 해서 팀 저장소에 자동으로 포함하지 않으며, 내용과 권리는 [공유 제외 기록](../ai_docs/HISTORY.md)을 확인합니다.

<a id="assetsscenes는-지워도-되나요"></a>

## Assets/Scenes 삭제 상태

초기 템플릿의 SampleScene이 들어 있던 폴더입니다. 사용자 삭제 뒤에도 ProjectSettings/ProjectSettings.asset의 templateDefaultScene이 삭제된 경로를 가리켜, 이번에 이 값을 빈 값으로 정리했습니다. 삭제된 씬을 복원할 필요는 발견하지 못했습니다.

실제 게임 씬은 Assets/_Project/Scenes/Main입니다. Main·Sandbox의 빌드 경로와 GUID, MapAuthoring 씬의 존재를 파일 기준으로 확인했습니다. 게임 코드에서 삭제된 샘플을 로드하는 참조는 발견하지 못했습니다.

위 내용은 2026-11-07의 설정·참조 조사 기록입니다. 당시 남아 있던 Settings/Scenes의 URP2DSceneTemplate은 2026-11-13에 연결된 Lit 2D 템플릿과 함께 정리했습니다. 실제 게임 씬과 기본 렌더링 설정은 보존했습니다.

<a id="assetssettings는-무엇에-쓰나요"></a>

## Assets/Settings 사용처


[UniversalRP.asset](../../Assets/Settings/UniversalRP.asset)

확인된 연결: GraphicsSettings와 품질 단계 6개 모두에서 사용

판단: 보존. 기본 렌더링 파이프라인입니다.


[Renderer2D.asset](../../Assets/Settings/Renderer2D.asset)

확인된 연결: UniversalRP의 기본 렌더러

판단: 보존. Main·MapAuthoring·Sandbox 카메라가 이 기본 설정을 따릅니다.


[UniversalRenderPipelineGlobalSettings.asset](../../Assets/Settings/UniversalRenderPipelineGlobalSettings.asset)

확인된 연결: GraphicsSettings의 URP 전역 설정

판단: 보존. 공통 렌더링 자원을 연결합니다.


[DefaultVolumeProfile.asset](../../Assets/Settings/DefaultVolumeProfile.asset)

확인된 연결: URP 전역 설정에서 참조

판단: 보존. Main의 별도 PostFxProfile과 다른 설정입니다.


[InputSystem_Actions.inputactions](../../Assets/Settings/InputSystem_Actions.inputactions)

확인된 연결: EditorBuildSettings의 전역 입력 에셋

판단: 보존 우선. Input System이 빌드에 사전 로드하고 활성화합니다.


Lit2DSceneTemplate.scenetemplate / Scenes/URP2DSceneTemplate.unity

2026-11-13 정리: 새 씬 창의 프로젝트 제공 Lit 2D 템플릿과 원본 씬입니다. 서로의 참조 외에는 게임·테스트·ProjectSettings에서 사용하지 않아 두 파일과 `.meta`, 빈 Scenes 폴더를 함께 백업 후 제거했습니다. 이 프로젝트의 새 씬 창에서 해당 템플릿 항목이 사라집니다.


렌더링 연결은 **ProjectSettings → UniversalRP → Renderer2D**, 그리고 **GraphicsSettings → URP 전역 설정 → DefaultVolumeProfile**입니다. `ProjectSetup.cs`에도 UniversalRP를 읽는 문자열 경로가 있어, GUID를 보존해 이동하더라도 이 경로를 함께 확인해야 합니다.

전투는 키보드·마우스를 직접 읽고 Main UI는 씬 내부의 DefaultInputActions도 사용합니다. 그렇다고 전역 InputSystem_Actions가 미사용인 것은 아닙니다. 현재 패키지가 자동으로 켜는 설정이며, 제거해도 입력 동작이 같은지는 별도 시험이 필요합니다.

<a id="정리하려면-무엇부터-확인하나요"></a>

## 정리 범위와 확인 항목

먼저 [작업 규칙](../ai_docs/WORKING_RULES.md)의 보존 범위를 확인합니다. 씬의 GUID뿐 아니라 코드의 경로 문자열, Resources 로드, 자동 검사와 에디터 메뉴도 사용처입니다. 렌더링 설정·전역 입력·Sandbox·리플레이 자료를 단순히 현재 게임 화면에 보이지 않는다는 이유로 지우지 않습니다.

Fusion의 가져온 실습 폴더 `Assets/Photon/FusionDemos/Fusion-Intro-Shared`와 Build Settings의 해당 씬 항목은 2026-11-13에 정리했습니다. SDK Hub가 참조하는 FusionDemos/FusionMenu의 `.unitypackage` 설치 원본은 SDK와 함께 버전 관리합니다. 설치 원본을 보관하는 것이 실습 씬을 다시 임포트한다는 뜻은 아닙니다. SDK 본체와 게임의 `Online/Fusion` 코드는 구분합니다. 실제 검증 범위는 [진행 현황](../ai_docs/progress/PROGRESS.md), 복원 위치는 [보관 안내](../ai_docs/HISTORY.md)에 있습니다.
