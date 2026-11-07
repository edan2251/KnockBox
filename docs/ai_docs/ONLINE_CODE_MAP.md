<a id="온라인-기능은-어디에서-고치나"></a>

# 온라인 코드 구조

방 만들기·참가·전송은 `Game/Online/`, 전투 판정은 `Simulation/`에 있습니다. 현재 구조가 이 역할을 이미 구분하므로 이번 이관에서는 파일이나 asmdef를 옮기지 않았습니다. 작업 원칙은 [WORKING_RULES](WORKING_RULES.md), 실제 검증 결과는 [이관 기록](HISTORY.md)을 봅니다.

<a id="작업할-파일-찾기"></a>

## 기능별 담당 코드

아래 경로는 `Assets/_Project/Scripts/` 기준입니다.


방 만들기·코드 참가·연습 버튼

먼저 볼 위치: `Game/Online/OnlineLobby.cs`, `Game/UI/Frontend/FrontendController.cs`

담당하는 일: 화면 명령과 로비 상태 연결


접속 시작·종료, P1/P2 입력, 재대결

먼저 볼 위치: `Game/Online/OnlineMatch.cs`

담당하는 일: 온라인 세션의 시작·종료, 로컬 슬롯, 시작 설정·시드·맵 확정


Relay 연결이나 전송 문제

먼저 볼 위치: `Game/Online/RelayTransport.cs`

담당하는 일: Unity Services 초기화, 익명 인증, Relay 할당·참가, DTLS 전송


전송 방식·지연·손실 모의 검사

먼저 볼 위치: `Game/Online/Transports.cs`

담당하는 일: 전송 인터페이스와 UDP·메모리·모의 전송


입력 패킷·시작 규격·틱 동기화

먼저 볼 위치: `Game/Online/LockstepSession.cs`

담당하는 일: 상대 입력을 확인하고 같은 틱을 진행하는 순서


상대와 등록 맵이 같은지 검사

먼저 볼 위치: `Game/Online/OnlineMapManifest.cs`

담당하는 일: 정렬된 맵 이름과 전체 지형 검증값 비교


경기 실행·설정·맵 선택·기록 재생

먼저 볼 위치: `Game/Bootstrap/MatchRunner.cs`

담당하는 일: 온라인·연습·리플레이가 공유하는 실행기


마우스·키보드·봇·화면 입력

먼저 볼 위치: `Game/Input/`

담당하는 일: 입력을 공통 InputFrame으로 변환


이동·공격·맵 충돌·무기·카드 판정

먼저 볼 위치: `Simulation/`

담당하는 일: 경기 규칙과 상태 저장/복원, 체크섬, 리플레이 데이터


캐릭터·무기·UI·화면 전환 표시

먼저 볼 위치: `Presentation/`, `Game/UI/Frontend/`

담당하는 일: 경기 상태를 화면에 표시하고 메뉴 명령 전달


<a id="연결에서-경기까지-따라가기"></a>

## 연결과 경기 시작 순서

1. `FrontendController`나 `OnlineLobby`가 방 생성·참가 명령을 전달합니다.
2. `OnlineMatch`가 세션을 관리하고 `RelayTransport`가 상대와 연결합니다.
3. `LockstepSession`이 시작 규격과 틱별 입력을 주고받습니다.
4. `MatchRunner`가 같은 조건으로 틱을 진행하고, `Simulation`이 전투 결과를 계산합니다.
5. 표시 담당이 결과를 캐릭터와 UI에 반영합니다.

연습과 리플레이도 같은 실행기와 판정을 사용합니다. 온라인에서 호출된다는 이유로 `FighterCombat`이나 맵 충돌 코드를 Online 폴더로 옮기지 않습니다.

실제 어셈블리 경계도 이 구분을 따릅니다. `Knockbox.Game`은 Simulation·Presentation·Input System·UGS·Transport 등을 참조합니다. `Knockbox.Simulation`은 참조 배열이 비어 있고 `noEngineReferences: true`여서 UnityEngine 없이 판정합니다. Simulation에 Relay나 화면 의존성을 추가하지 않습니다.

## 같은 경기로 연결되기 위한 조건

2026-11-06 코드 기준 온라인 프로토콜은 **9**, 입력 기록 형식은 **v8**입니다. `LockstepSession.ProtocolVersion`과 `InputRecording.FormatVersion`에서 확인할 수 있습니다. 과거 문서의 프로토콜 6~8은 그때의 기록입니다.

- 양쪽은 시작할 때 설정·등록 맵 목록·지형을 비교하고 같은 시드를 사용합니다.
- 등록 목록은 내부 ID 순서로 맞춘 뒤 결정론적 순환에 사용합니다. 목록과 맵 편집 방법은 [팀 안내](../team/TEAM_WORKFLOW.md)에 있습니다.
- 다른 프로토콜은 경기 시작 전에 거절합니다. 온라인 규격 변경과 과거 리플레이 지원은 따로 검토해야 합니다.
- 구 리플레이의 설정 후보·기대 체크섬과 현재 온라인의 맵 검증값을 같은 것으로 취급하지 않습니다.

기존 Editor와 Player 사이에서 중간 틱에 1ULP 차이, 즉 부동소수점 표현의 최소 단위 차이가 확인된 기록이 있습니다. 정확한 연산 원인은 아직 입증하지 못했습니다. [유지보수 기록](HISTORY.md)의 확인 범위를 따르며, 자동 검사 통과를 모든 실행 환경의 동일성 보장으로 확대하지 않습니다.

<a id="unity-cloud와-mcp는-구분하기"></a>

## Unity Cloud와 MCP

저장소를 새 위치로 복사해도 기존 Unity Cloud 프로젝트 연결이 다른 프로젝트로 바뀌지는 않습니다. 이번 이관에서는 연결·권한·과금 설정을 바꾸지 않았습니다. 식별자·비밀값·참가 코드는 이 문서에 적지 않습니다.

팀원이 처음 열면 Unity Hub 로그인과 Project Settings의 Services 연결을 확인합니다. 팀원의 Dashboard 접근 권한과 실제 두 PC Relay 사용은 별도 확인 대상입니다. 배치 빌드의 Services 경고만으로 프로젝트 연결이 삭제되었다고 단정하지 않습니다. 자세한 확인 결과는 [이관 기록](HISTORY.md)에 있습니다.

MCP는 개발 도구가 Unity 에디터를 제어하는 연결입니다. 게임의 Relay 전송과 별개입니다. 최근 MCP 서버·에디터 연결 조치를 온라인 전투 코드 변경이나 Relay 실기 검증으로 기록하지 않습니다.

<a id="수정-후-무엇을-확인하나"></a>

## 수정 후 검증

1. 컴파일과 해당 기능의 기존 테스트를 확인합니다.
2. 전송·입력 변경이면 `OnlineLockstepTests`, 맵 목록 변경이면 `RegisteredMapRotationTests`를 확인합니다.
3. 판정이나 상태 변경이면 결정론·상태 저장/복원·체크섬·리플레이 검사도 확인합니다.
4. Main에서 로비·연습·VS·카드·결과 흐름이 유지되는지 봅니다.
5. 자동 검사, 로컬 실행, 실제 두 PC Relay 시험 결과를 나누어 기록합니다. 현재 두 PC 실기는 미검증입니다.

앞으로 전송 구현이 늘어 찾기 어려워지면 `Online/Transport`, `Online/Session` 정도의 작은 하위 폴더를 검토할 수 있습니다. 지금 당장 필요한 이동은 아닙니다. 이동 전에 `.meta` GUID, asmdef, 문자열 경로, 직렬화 참조와 테스트를 확인합니다.
