# Unity 팀 작업 안내

Unity에서 게임 화면과 맵을 직접 편집할 때 보는 안내입니다. **2026-11-11, main `60d605a`의 Main·MapAuthoring 구조**를 기준으로 합니다. 맵 제작 버튼과 순서는 [맵 제작 사용 안내](MAP_AUTHORING.md)를 봅니다.

작업 위치·Git·보존·검증 원칙은 [현재 작업 규칙](../ai_docs/WORKING_RULES.md)을 따릅니다. 원본 프로젝트의 개발 브랜치는 `exp/brawl-v1`였습니다. 과거 작업의 통과 기록을 새 위치의 이관 검증 결과로 보지는 않습니다.

[전체 문서 안내](../README.md) · [아트 작업](ART_WORKFLOW.md) · [온라인 코드 지도](../ai_docs/ONLINE_CODE_MAP.md) · [커밋 규칙](COMMIT_RULES.md) · [과거 검증 보관](../ai_docs/HISTORY.md)

<a id="1-작업할-씬을-고르세요"></a>

## 1. 작업 씬

- **게임·UI:** `Assets/_Project/Scenes/Main.unity`
- **맵 배치·저장·시험:** `Assets/_Project/Scenes/MapAuthoring.unity`
- **자동 검사·문제 재현:** `Assets/_Project/Scenes/Sandbox.unity`

팀이 여는 씬은 `Assets/_Project/Scenes/`에 있습니다. 초기 Unity 템플릿의 `Assets/Scenes/`는 사용자 삭제 후 기본 경로를 정리한 상태입니다. 기본 폴더와 렌더링·입력 설정의 용도는 [프로젝트 폴더 안내](PROJECT_FOLDERS.md)에 정리합니다.

Main의 `Match`에는 경기 실행, 화면 표시, 온라인·로비·화면 전환 담당이 연결되어 있습니다. 카메라와 주요 연출 오브젝트도 Play 전에 배치되어 있습니다.

`Frontend___edit_these_screens`는 **실제 실행에도 사용하는 UI 프리팹 인스턴스**입니다. Main에 참조가 빠졌다고 코드가 별도 UI를 새로 만들어 대신하지 않습니다.

같은 씬·프리팹은 한 번에 한 사람이 맡도록 작업 대상을 나누는 방식을 권장합니다. 기능별 브랜치 운영도 팀 합의가 필요한 권장안입니다. 에셋 전달과 충돌 회피 절차는 아트 안내를 참고하세요.

<a id="2-ui를-편집하고-저장하세요"></a>

## 2. UI 편집과 저장

1. Main에서 `Frontend___edit_these_screens`의 `FrontendView`를 선택합니다.
2. Inspector의 `편집 화면`에서 메인, 코드 참가, 로비 대기, 대결 소개, 카드 선택, 경기 결과 중 하나를 고릅니다. `선택한 화면 열기`를 누르면 Game 뷰나 2D Scene 뷰에서 확인할 수 있습니다.
3. 자식의 **RectTransform, Text, Image**를 직접 편집합니다. 위치·크기·회전·글자 크기·폰트·색상은 해당 오브젝트에 저장됩니다.
4. Main에만 적용하려면 씬을 저장합니다. 공용값으로 사용할 변경은 해당 중첩 프리팹에 의도한 항목만 Apply합니다.
5. **저장 → 씬 다시 열기 → Play → 로비 왕복**으로 확인합니다. 편집 중 어떤 화면을 열었든 Play는 메인에서 시작합니다.

Play 중 바꾼 값은 종료하면 사라집니다. 애니메이션 대상은 기존 움직임에 편집한 차이를 더해 표시합니다. 카드 크기를 바꾸면 손 접점도 실제 카드 아래쪽을 따라갑니다.

<a id="어떤-프리팹을-편집하나요"></a>

### 편집 대상 프리팹

주요 프리팹은 `Assets/_Project/Prefabs/Frontend/`에 있습니다.

- `Frontend`: 전체 화면 묶음과 참조, Canvas/EventSystem
- `MainAndLobby`: 메인 버튼·로고·방 코드 입력·대기 화면
- `DuelCardsResult`: VS·카드·결과가 공유하는 두 캐릭터, 배경 패널, 명판, 선택 UI
- `AcquiredCards`: 보유 카드
- `GameplayHud`: 경기 HUD
- `WeaponVisual`: 과거 무기 표시 보존. Main의 `Brawl_Weapon` 루트는 활성이며, 무기가 꺼진 현재 경기에서는 코드가 모델·표시 자식을 숨깁니다.
- `PostFxProfile.asset`: 후처리 기본 설정. 실행할 때 컴포넌트까지 복제하므로 타격 펄스가 원본을 수정하지 않습니다.

### 편집값과 실행 코드의 역할

화면을 보여주거나 숨기는 담당은 `FrontendController`입니다. 경기·로비 상태를 읽어 화면을 고릅니다. 전환 곡선과 타이밍은 `FrontendMotion`과 View의 연출 코드가 담당합니다.

주요 오브젝트를 삭제하거나 참조를 끊지 마세요. `CaptureAuthoredLayout`과 `MaintenanceSceneMigration.Run`은 최초 이전 도구입니다. 일상 편집에서 다시 실행하지 않습니다.

`FrontendTheme`의 폰트·초상 크기는 새 테스트 화면을 만들 때의 기본값입니다. 이미 배치된 Main은 **실제 Text/RectTransform**에서 폰트와 크기를 고칩니다. 플레이어 색, 카드 호버, 준비 여부처럼 상태에 따라 달라지는 색에는 Theme/Palette와 연출 코드도 관여합니다.

UI 입력(`KNOCKBOX_UI_input`)의 액션 연결은 `Frontend.prefab`에서는 비어 있고, Main 씬의 오버라이드가 씬에 내장된 `DefaultInputActions` 사본을 가리킵니다. Input System 1.20은 연결이 비어 있으면 활성화 때 기본 액션을 넣습니다. UI 입력을 바꾸려면 이 구조를 먼저 확인하고, 인스턴스 Revert나 Apply로 정리하지 마세요.

UI 초상은 실제 FighterView에서 가져온 부품으로 저장되어 있습니다. 게임 캐릭터의 스프라이트 형태를 바꾸면 저장된 UI 초상도 함께 확인하세요. 자동 외형 재동기화 도구는 현재 범위에 없습니다.

<a id="3-설정이-실제로-어떻게-적용되는지-확인하세요"></a>

## 3. 설정 적용 순서

`Match/MatchRunner` Inspector 하단의 **실제 적용 설정**을 봅니다. 최종 몸 크기·체력·충전 틱·설정 체크섬을 확인할 수 있습니다.


`Data/Tuning/MovementTuning.asset`, `CombatTuning.asset`

담당하는 값: Inspector에 노출된 이동·전투 원본 수치


`BrawlParams`

담당하는 값: Brawl용 변환과 기존 고정 전투 규칙. 몸 크기는 원본 0.9 → 최종 0.72


`CardCollectionRules`

담당하는 값: 2라운드 선승·5세트 선승·효과 없는 카드 획득 등 현재 UI 경기 규칙


Main의 `mapCatalog`, `selectedMap`

담당하는 값: 등록 목록이 있으면 라운드 순환. 목록이 비었을 때만 selectedMap 정의(없으면 Brawl)로 시작


`FindRecordedConfig`, `ResolveRecordedArena`

담당하는 값: 과거 기록의 설정 후보·옛 맵 텍스트 해석


Inspector의 원본 수치가 Brawl에서 그대로 쓰인다고 가정하지 마세요. 현재 최소 돌진은 **준비 8틱 + 충전 12틱**입니다. 기존 값이며, 이번 안내에서 새 밸런스를 정한 것이 아닙니다.

- 로컬 연습은 다음 라운드로 넘어갈 때 튜닝을 다시 읽습니다.
- 온라인·입력 녹화·재생 중에는 경기 설정을 고정합니다.
- 과거 리플레이용 설정 후보와 기록 해시는 수정하지 않습니다.

`Knockbox/Setup`은 과거 초기화 메뉴라 일상 편집에 필요하지 않습니다. 배치된 Main에서 `Ensure Scene Objects`를 실행하더라도 Inspector 설정을 다시 덮어쓰지 않도록 보호되어 있습니다.

<a id="4-맵은-brawl-사본에서-시작하세요"></a>

## 4. 맵 제작

[맵 제작 사용 안내](MAP_AUTHORING.md)

생성 종류, 오브젝트별 편집, 로프 연결, 저장·재열기·미등록 시험과 CP5 검수 순서를 한 문서에 모았습니다. MapAuthoring Inspector의 **맵 제작 사용 안내**에서도 엽니다.

<a id="맵-열기·배치·저장"></a>
<a id="오류로-처리되는-배치"></a>
<a id="물리-상자"></a>
<a id="로프-연결과-연결-상자"></a>
<a id="로프-묶음-복제와-기존-맵"></a>
<a id="고정-지형-두-점-절단"></a>
<a id="미등록-시험과-정식-등록"></a>
<a id="독립-로프"></a>

기존 맵 제작 링크는 [상세 안내](MAP_AUTHORING.md#맵-파일과-오브젝트)로 이어집니다.

<a id="5-스냅대칭테마를-사용하세요"></a>

## 5. 스냅·대칭·테마

<a id="좌표-정밀도와-스냅"></a>
<a id="좌우-대칭-복사"></a>
<a id="색상과-배경-그라데이션"></a>

[배치와 색상 설정](MAP_AUTHORING.md#스냅대칭테마)

격자·상자 윗면·발 위치 스냅, 대칭 복사, 맵 테마의 사용법입니다. 같은 절차를 이 문서에 중복하지 않습니다.

## 6. 맵 표시와 판정의 범위

맵은 **정의 에셋 → 순서가 보존된 ArenaDef**로 전달됩니다. 시뮬레이션 판정과 ArenaView 표시가 같은 데이터를 사용합니다.

Main의 지형 자식은 이 데이터로 만든 결과물입니다. 지형은 MapAuthoring에서 바꾸세요. Main에서 생성된 지형의 Transform을 직접 바꾸어도 경기에서는 정의 좌표로 돌아갑니다.

### 테두리 연출의 인식 방법

현재는 위치와 크기로 자동 판정합니다. `Solid` 지형의 min/max가 네 맵 경계 중 하나에 닿거나 넘어가면 공통 테두리 묶음으로 확대·축소합니다. 허용 오차는 **0.001**입니다.

이름·ID는 사용하지 않으므로 같은 위치에 다시 만들면 같은 연출을 받습니다. 맵 끝에 닿는 내부 발판도 포함될 수 있습니다. 표시만 움직이며 실제 판정은 원래 좌표를 유지합니다.

이 방식은 이번 유지보수에서 바꾸지 않았습니다. 테두리를 고정 생성할지, 명시적으로 지정할지는 사용자 결정 대기입니다.

### 현재 지원 범위

- **사각/볼록 고정 지형과 질량을 지정하는 물리 상자**를 지원합니다.
- 마름모·사다리꼴 등 볼록 지형은 두 점 절단으로 만듭니다. 원형 지형과 별도 도형 생성 프리셋은 미구현입니다.
- 일반 로프 연결망과 독립 파괴 연결 상자는 맵 제작 안내를 사용합니다. 줄 외의 동적 구조물·경첩 제작은 보류 상태입니다.
- 기존 TXT/JSON 맵은 옛 맵·Sandbox·리플레이를 위해 남깁니다.
- 신규 맵은 `map:<ID>`로 구분합니다.

새 맵 리비전을 기록에 통째로 저장하는 기능은 없습니다. 같은 ID의 정의를 바꾼 뒤에도 옛 기록이 계속 재생된다고 보장할 수 없습니다.

### 온라인·기록 호환

온라인 프로토콜은 **9**입니다. 양쪽의 같은 순서의 맵 이름과 전체 지형 검증값을 비교합니다. 연결 대기 중 확정한 설정·맵 배열을 경기 내내 사용하며, 프로토콜 8 이하는 경기 시작 전에 거절합니다.

기존 맵의 입력 기록 **v8**과 과거 맵·상태 체크섬은 유지합니다. 제작기 물리 상자가 들어간 경기 목록은 **v9**로 기록하며 상자 위치·크기·질량·ID/순서까지 검사합니다. v9는 기록 당시의 맵 정의가 다르면 재생하지 않고, 이전 빌드에서는 읽지 못합니다. 새 기록에도 맵 전체가 들어 있지는 않습니다. 일반 라운드와 세트 후 라운드 모두 연속 중복을 피합니다. 다만 재대결은 새 경기로 다시 섞기 때문에 직전 경기의 마지막 맵과 같을 수 있습니다.

당시 맵 순환 작업에서는 새 빌드를 만들지 않았습니다. 새 위치의 빌드·실행 결과는 [과거 검증 보관](../ai_docs/HISTORY.md)을 확인하세요.

<a id="7-실행-중-생성되는-대상을-구분하세요"></a>

## 7. 실행 중 생성 대상

- `MatchRunner`는 고정 틱·입력·경기 설정·리플레이를 담당합니다. `MatchPresenter`는 사건을 받아 각 표시 담당으로 전달합니다.
- `FrontendController`는 상태에 따라 화면과 메뉴 명령을 처리합니다. `FrontendView`/`FrontendLayout`은 저장된 화면, 편집 차이, 기존 전환 연출을 사용합니다. Main은 새 Canvas나 버튼을 만들지 않습니다. 이 필수 검사와 제작기 시험 연결은 씬 이름 `Main`으로 판별하므로 씬 이름을 바꾸지 마세요.
- `MatchPresenter`의 화면 담당 참조가 빠지면 오류를 냅니다. 다만 카메라 리그·팔레트·도형 라이브러리, FighterView의 Pose·팔다리 부품, PostFx Volume은 비어 있으면 실행 중에 새로 만듭니다. 이 참조를 지우지 마세요.
- `Match/Arena`의 지형 자식은 Play할 때 지우고 맵 정의에서 다시 만듭니다. 지형은 이 자식이 아니라 MapAuthoring과 맵 정의에서 편집합니다.
- 무기 상태·탄약·발사체·카드 획득 판정은 Simulation에 있습니다. 무기 표시는 배치된 WeaponVisual을 재사용합니다.
- 투사체·충돌 효과·먼지·KO 조각은 정해진 최대 수의 풀에서 재사용하거나 필요할 때 만드는 일시적 표현입니다. 담당 컴포넌트는 Main에 배치되어 있고 ShapeLibrary/Palette 참조를 볼 수 있습니다. 모든 개별 조각을 프리팹으로 만든 것은 아닙니다.
- 맵별 투영 그림자 메시와 KO 전용 충돌체는 정의에 따라 만듭니다. KO의 Unity 물리는 시각 효과 전용입니다.
- 네트워크 세션·Relay 전송은 방 생성·참가 때 만듭니다. OnlineMatch/OnlineLobby 담당은 미리 배치되어 있습니다.
- Sandbox와 자동 테스트는 기존 절차식 생성 경로를 유지합니다. Main에서 별도 UI 복제본을 사용하는 것은 아닙니다.

<a id="8-저장한-변경을-검수하세요"></a>

## 8. 저장 후 검수

Main은 다음 순서로 확인합니다.

**메인 → 코드 참가/복귀 → 연습 → 첫 VS → O/P 테스트 KO → 카드 획득 → 다음 라운드 → 결과/O/X**

MapAuthoring의 시험 사본도 저장·재열기·실행을 확인합니다. 실제 두 PC Relay와 사람이 느끼는 연출감은 자동 검사만으로 완료 판정하지 않습니다.

`Window → General → Test Runner`에서는 다음 검사를 우선 확인합니다.

- `MapAuthoringTests`
- `AuthoredMainTests`
- `MaintenanceSafetyTests`
- `PresentationRulesTests`

경기·온라인 변경에는 관련 결정론, 저장·복원, 락스텝, 리플레이 검사도 필요합니다. 실행 방식과 에디터·배치 병행 제한은 [현재 작업 규칙의 검증과 보고](../ai_docs/WORKING_RULES.md#검증과-보고)를 따릅니다.

<a id="변경별-검사"></a>

### 변경별 검사

변경한 범위에 맞는 줄만 실행합니다. 검사는 사람이나 AI가 직접 실행합니다. CI와 Git 훅은 설치되어 있지 않아 자동으로 돌지 않습니다. Unity 검사는 열린 편집기의 Test Runner나 Unity MCP `run_tests`로 실행하고, 같은 프로젝트를 배치 모드로 따로 열지 않습니다.

- **문서만:** `python Tools/check_docs.py`. Unity 검사는 필요 없습니다.
- **C# 주석만:** 컴파일 확인.
- **제작기·맵 데이터:** 컴파일 + 바뀐 기능의 `Map*` 검사 + `MapAuthoringTests` + `MaintenanceSafetyTests`(기준 맵 해시).
- **Presentation·UI:** 컴파일 + `PresentationRulesTests`·`PaletteTests` + 관련 PlayMode(`AuthoredMainTests`, `FrontendFlowTests`) + 사용자 Play.
- **Game(실행기·입력·온라인):** 컴파일 + `OnlineLockstepTests`·`RegisteredMapRotationTests`·`MaintenanceSafetyTests` + 관련 PlayMode.
- **Simulation(판정·물리·상태):** 컴파일 + `StateChecksumTests`·`SimulationRulesTests`·`SyncTestTests`·`DeterminismTests`·`BrawlInputCompatibilityTests` + 바뀐 기능의 검사 + `RopeGraphReplayTests`(CP5 물리 기준).
- **설정·기록 형식·맵 schema·프로토콜:** Simulation 검사 + 전체 EditMode.
- **씬·Prefab:** 컴파일 + `NamingConventionTests` + `AuthoredMainTests` + 사용자 Play.
- **광범위한 변경, 주요 체크포인트 완료:** 전체 EditMode 한 번 + 관련 PlayMode.

`Slow`와 `LegacyCombat` 카테고리는 이름만 보고 빼지 않습니다. 바뀐 코드가 무기 경로나 해당 측정에 닿으면 포함합니다(폼 전투는 2026-11-11 삭제). 기존 실패와 새 실패를 구분해야 할 때만 변경 전에 같은 관련 검사를 실행합니다.

검사가 실패하면 기대값을 새 결과로 바꾸거나 조건을 약하게 하지 않습니다. 원인을 고치고, 의도한 동작 변경이라면 사용자 승인을 받은 뒤 같은 변경에서 기대값과 기록을 함께 고칩니다.

<a id="9-이름과-복구-자료를-보존하세요"></a>

## 9. 이름 규칙과 복구 자료

`.meta`는 대응 에셋과 함께 보존합니다. 생성 오브젝트는 **영문·숫자·밑줄**만 사용하고 공백은 _로 바꿉니다. 사용자에게 보이는 한글 문구와 오브젝트 내부 이름을 분리합니다. 파일 이름의 기존 문자·숫자·밑줄 규칙은 유지하며, 한글 폰트·외부 원본을 오브젝트 규칙 때문에 일괄 변경하지 않습니다.

2026-11-06 승인된 이름 정리는 게임 관리 에셋과 씬·프리팹 이름에만 적용했습니다. 확장자·GUID·맵 ID·어셈블리 내부 이름·패키지·LocalOnly·기록/백업·외부 원본은 바꾸지 않습니다.

LocalOnly·incoming·Recordings·Captures·구 빌드·최신 경기 기록은 Git만으로 복구할 수 없습니다. 당시 유지보수에서는 삭제하지 않았습니다.

- 복구 기준·미해결 호환성: [유지보수 작업 기록](../ai_docs/HISTORY.md)
- 맵 편의 도구의 작업 기록: [맵 도구 개선](../ai_docs/HISTORY.md)
- 현재 보존·백업 권한: [현재 작업 규칙](../ai_docs/WORKING_RULES.md)
- 이관의 복사·제외·복원 검증 결과: [과거 검증 보관](../ai_docs/HISTORY.md)

## 10. 에디터 Play가 일정 간격으로 끊기면

일반 플레이 검수에서는 **MCP 설정 창 자체를 닫아보세요.** 서버가 꺼져 있어도 설정 창이 주기적으로 접속을 확인할 수 있습니다. Disconnect나 서버 중지만 누르고 창을 열어두는 것과는 다릅니다.

AI 제어가 필요하면 연결을 유지한 채 창만 닫아도 됩니다. 게임 실행과 배포판에는 MCP 서버가 필요하지 않습니다.

확인한 지연 경로·측정값·남은 에디터 GUI 오류는 [연습모드 성능 조사](../ai_docs/HISTORY.md)에 있습니다. 임의로 패키지를 업그레이드하거나 캐시 코드를 직접 수정해 해결하지 않습니다.
