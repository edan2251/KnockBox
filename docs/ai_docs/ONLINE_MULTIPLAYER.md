# 온라인 멀티플레이 구현 기준

2026-11-13 현재 구현을 기준으로 한다. 서버 연결·입력 지연·예측/롤백·카드·리플레이를 수정하는 AI와 개발자가 먼저 읽는 문서다. 코드 구조와 수정 시 지켜야 할 계약을 유지하며, 일회성 작업 계획·배포 파일 목록은 누적하지 않는다. 현재 승인과 최근 검사 결과는 [PROGRESS](progress/PROGRESS.md), 보관 증거는 [HISTORY](HISTORY.md)를 따른다.

## 현재 구현

**Photon Fusion Host/Client 기반 1대1 게임이다.** 방을 만든 PC가 전투 판정을 확정하는 Host이고, 참가 PC는 입력을 예측한 뒤 Host 상태로 복원·재실행한다. 별도 전용 게임 서버를 배포한 구조가 아니다. 일반 에디터 실행과 Windows 빌드 모두 이 경로를 사용한다.

| 항목 | 현재 기준 |
|---|---|
| SDK | Fusion 2.1.4 Stable Build 2500, 저장소의 `Assets/Photon`과 `.meta` 포함 |
| 역할 | 방 생성 PC = Host/P1, 참가 PC = Client/P2, 최대 2인 |
| 지역·기본 버전 | `kr`, `knockbox-fusion-1` |
| 실제 접속 버전 | `knockbox-fusion-1-prediction1-snapshot8692-tick60-delay3` |
| 틱·송신 설정 | 전투·SDK Client/Server·송신 모두 60Hz |
| 입력 지연 | SDK 최소 입력 지연 3틱, 자동 추가 지연 끔, 앱 지연 큐 중복 적용 없음 |
| 접속 UI | 기존 방 생성·8자리 코드 참가, 이름 설정 없이 P1/P2 |
| 종료 | Host 이탈 시 경기 종료. 호스트 이전·재접속 복원은 미구현 |

입력 지연은 `FusionPredictionPolicy.DelayTicks = 3` 한 곳에서 관리한다. 60Hz에서 설정상 50ms지만 통신 RTT나 버튼-화면 총 지연을 뜻하지 않는다. SDK의 `MinimumInputDelay`를 Ticks 단위로 설정하고 `ClientsCanAddInputDelayToLimitResims = false`로 자동 추가 지연을 끈다. 결합된 전체 전투 상태를 복원하므로 `ClientsOnlyRollbackUpdatedObjects = false`다. 기존 앱 입력 지연 큐를 중복 적용하지 않는다.

지역·기본 버전은 `Assets/_Project/Resources/FusionConnectionSettings.asset`에서 읽는다. `FusionPredictionPolicy.CreateConfig`가 SDK 전역 설정의 접속 전용 사본에 60Hz/3틱 정책을 적용하며 등록 프리팹 테이블·비직렬화 설정을 보존한다. 전역 SDK 에셋을 실행 중 덮어쓰지 않는다. 실제 접속 버전은 기본 버전 뒤에 스냅샷 규격·틱 주기·지연값을 붙여 이전 빌드와 분리한다.

이전 Relay·직접 UDP 접속, Send64/128·Prediction2/4 선택 분기, `OnlineTimingDiagnostics`는 제거됐다. 현재 진단은 `FusionPredictionDiagnostics`다. 아래의 공통 세션과 오프라인 검사 경로는 실제 사용 중이므로 옛 파일 이름만 보고 제거하지 않는다. Main/MapAuthoring/Sandbox, 기존 UI 배치·전투 규칙·60Hz·경기 RNG·리플레이 v8/v9를 보존한다.

## 코드 지도

아래 Game/Simulation 경로는 `Assets/_Project/Scripts/` 기준이다. 경로 없이 적힌 Fusion 관련 파일은 `Game/Online/Fusion/`에 있고, `LockstepSession.cs`·`OnlineMapManifest.cs`는 `Game/Online/`에 있다. 네트워크·판정·화면을 분리한 이유는 카드 기능을 늘릴 때 연결 코드를 수정하지 않고 같은 규칙을 연습·온라인·리플레이에서 실행하기 위해서다.

| 책임 | 담당 파일 | 경계 |
|---|---|---|
| 방 화면·명령 | `Game/Online/OnlineLobby.cs`, `Game/UI/Frontend/FrontendController.cs` | 생성·참가·취소·나가기를 요청한다. 전투 판정을 하지 않는다. |
| Runner·방 수명 | `Game/Online/Fusion/FusionConnection.cs` | Host/Client 생성·종료·실패·콜백 소유권, SDK OnInput 전달 |
| 접속 설정·고정 시간 정책 | `FusionConnectionSettings.cs`, `FusionPredictionPolicy.cs` | 지역·버전과 60Hz/3틱 정책 분리. App ID는 로컬 SDK 설정에서 읽는다. |
| 코드·종료 계약 | `FusionRoomCode.cs`, `ITransportLifetime.cs` | 경기 RNG를 쓰지 않는 코드 생성·정규화, 연결 종료 통보 |
| 제어 메시지 전송 | `FusionTransport.cs`, `FusionPacketCodec.cs` | 시작 합의·heartbeat·체크섬·종료의 RPC 전송, 제한된 분할/재조립 |
| 경기 연결·수명 | `Game/Online/OnlineMatch.cs`, `FusionPredictionSession.cs` | 준비된 설정/맵으로 시작, 입력·예측·확정 경로 연결, 기록 종료 |
| 시작 조건 합의 | `LockstepSession.cs`, `OnlineMapManifest.cs` | 프로토콜 9·설정·맵 목록/전체 지형·시드, 연결 상태·확정 체크섬 |
| SDK 입력 | `FusionPlayerInput.cs` 안의 `FusionPlayerInput`, `FusionInputCollector` | OnInput 수집, 카드 클릭 의도 유지, 오래된 라운드 입력 거절 |
| 복원·전투 실행 | `FusionPredictionWorld.cs`, `FusionWorldSnapshot.cs` | 전체 SimState 스냅샷과 FixedUpdateNetwork 실행. Transform 동기화로 판정을 대신하지 않는다. |
| 확정 입력·사건·리플레이 | `FusionConfirmedJournal.cs` | 순서/체크섬 검증 후 입력 기록과 카드/결과/일회성 사건 확정 |
| 기존 씬 수명 | `FusionStateSceneManager.cs`, `Assets/_Project/Editor/FusionPredictionPrefab.cs` | Main/UI를 다시 로드하지 않고 Resources 상태 전용 프리팹만 Spawn |
| 관측 | `FusionPredictionDiagnostics.cs` | 초당 CSV, 네트워크·재실행·입력 나이·프레임 관측. 판정 변경 없음 |
| 공통 실행·표시 | `Game/Bootstrap/MatchRunner.cs`, `MatchPresenter.cs` | 예측 그림과 확정 이벤트/Frontend 상태를 구분 |
| 카드 요청·연출 | `Game/Input/FrontendInputSource.cs`, `Game/UI/Frontend/FrontendView*.cs` | 입력으로 선택 전달, 확정 상태 표시. 능력을 직접 지급하지 않는다. |
| 카드·전투 판정 | `Simulation/Data/CardCollectionRules.cs`, `AugmentDef.cs`, `Simulation/Systems/MatchFlow.cs`, `Augments.cs` | UnityEngine·Photon을 참조하지 않는 공통 결정론 계산 |

`LockstepSession`이라는 이름이 남아 있어도 실제 Photon 전투 입력은 그 지연 큐를 거치지 않는다. 현재 접속에는 시작 합의·heartbeat·체크섬·종료가 필요하므로 클래스를 삭제하지 않는다. `ITransport`, `MemoryTransport`, `SimulatedLink`와 OnlineMatch의 비-Fusion 분기는 계정 없이 돌리는 결정론·입력 처리 순서 회귀 검사에서 사용한다. 로비에서 선택할 수 있는 이전 멀티 방식은 아니다. 함수 이름 `Rpc_LockstepFragment`도 제어 메시지 경로와 RPC 회귀 검사를 위해 유지한다.

## 실행과 확정 경계

1. 기존 UI가 `FusionConnection`으로 방을 만들거나 참가한다. 코드는 8자리이며 I/O/0/1을 제외하고, 참가 입력의 대소문자·앞뒤 공백을 정규화한다. Client는 잘못된 코드로 새 방을 만들지 않는다.
2. `OnlineMatch`가 준비한 설정·맵 검증값과 시드를 `LockstepSession`으로 합의한다. 런타임 단조 시계는 `Time.realtimeSinceStartupAsDouble`이며 송수신 시점에 읽는다.
3. 준비 후 Host가 `FusionPredictionWorld`를 Spawn한다. `OnInput`에서만 외부 입력을 읽고 SDK 버퍼에 넣는다. 재실행 중 키보드·마우스·UI 큐를 다시 읽지 않는다.
4. `FixedUpdateNetwork`마다 SDK가 복원한 Snapshot을 SimWorld에 넣고 Step한다. Host가 권위 상태를 확정하고 Client가 예측·복원·재실행한다. 이때 MatchRunner의 일반 Update/StepOnce 루프는 전투를 중복 진행하지 않는다. Host 자체는 롤백하지 않으며 양쪽 대등한 GGPO 구조나 전용 서버는 아니다.
5. 전투 그림은 `MatchRunner.World`, 카드·결과 UI와 일회성 사건은 `FrontendWorld/EventWorld`를 사용한다. 재실행에서 소리·획득·효과·리플레이 기록을 중복 발생시키지 않는다.

스냅샷은 동일 Windows 64비트 빌드의 **전체 SimState 8,692바이트**다. RNG·카드/선택 단계·파이터·상자·로프 접촉·보류 상태까지 복원한다. 저장 파일 규격이 아니며 타 플랫폼 지원 때 메모리 레이아웃을 재검토해야 한다. 필드 추가뿐 아니라 크기가 같은 순서/의미 변경에도 스냅샷 스키마·접속 버전과 전체 필드 왕복/복원 검사를 함께 바꾼다. Simulation에 Photon 의존성을 추가하지 않는다.

호스트 확정 입력은 매 틱 reliable RPC로 즉시 보낸다. 최대 4틱을 담는 표현이 남아 있어도 일부러 모아 기다리지는 않는다. Client의 `FusionConfirmedJournal`이 순서와 체크섬을 확인한 뒤에만 입력 기록·사건·UI를 확정한다. 중복은 재적용하지 않고 빈 구간·불일치는 경기 종료로 처리한다. 마지막 미도착 구간 때문에 두 리플레이 파일 길이는 다를 수 있으나 각 확정 구간은 자기 최종 체크섬으로 재생되어야 한다.

기존 v8/v9 입력 리플레이와 맵 schema 2~8·구 기록 설정 후보·기대 체크섬을 보존한다. 온라인 접속 버전과 리플레이 저장 버전은 별개다. 예전 Editor/Player 사이 중간 틱 float 최소 단위 차이의 정확한 원인은 입증하지 못했으므로 이번 자동 검사 통과를 모든 실행 환경의 동일성으로 확대하지 않는다.

현재는 기존 화면 보간을 사용하며 별도 보정 완화 연출은 없다. 예측이 틀리면 Client 위치가 보정될 수 있다. 설정 3틱은 매 순간 버튼-화면 지연이 정확히 50ms라는 뜻이 아니며 렌더링·네트워크·SDK 예측 시계가 각각 영향을 준다.

## 연결 수명과 실패 처리

- `FusionConnection`이 Runner 생성·종료를 소유한다. 실패하거나 종료한 Runner를 다시 쓰지 않고 새 연결을 만든다. 로비 요청의 취소·완료와 콜백 소유권을 보존해 이전 요청이 새 연결을 종료하지 않게 한다.
- 생성은 Host, 참가는 Client이며 최대 2인·비공개 방이다. App ID·지역·실제 접속 버전이 같아야 코드를 찾는다. 방에 들어간 뒤에는 설정·맵·프로토콜 합의까지 통과해야 경기를 시작한다.
- `FusionTransport`와 `ITransportLifetime`은 연결 종료를 공통 세션에 알린다. UI나 카드 코드가 임의로 Runner를 찾아 종료하지 않는다. 연결 서비스 종료와 입력 대기 시간 초과를 구분한다.
- Host 이탈 시 경기를 종료한다. 관전·3인 이상·재접속 복원·호스트 이전은 미구현이며 인원 설정만 늘려 해결할 수 없다.
- `FusionStateSceneManager`는 Main/UI의 로드·언로드를 맡지 않는다. 상태 전용 `FusionPredictionWorld` 프리팹만 Spawn한다. 씬 NetworkObject·Unity 물리를 추가하려면 이 계약부터 검토한다.
- 전송 조각의 중복·역순·불완전 데이터와 큐 상한을 검사한다. 네트워크에서 받은 카드 명령으로 능력을 직접 부여하거나 공통 입력 검증을 우회하지 않는다.

## 카드 능력과 연출 확장

현재 Main의 세 카드(한 방·맷집·멀리 던지기)는 **획득 목록에 쌓이는 단계이며 실제 능력은 아직 미구현**이다. 과거 Augments 계산 코드가 있다는 이유로 활성화 완료로 판단하지 않는다. 닉네임도 후속 기능이며 Fusion 식별자·P1/P2 슬롯과 구분한다.

선택은 `CardClicked → FrontendController.Choose → MatchRunner.RequestCardSelection → FrontendInputSource → FusionInputCollector → MatchFlow.StepSetPick` 순서다. Main의 모놀리스 선택 무대(`CardPickStage`)도 `FrontendController.ChooseCard`로 같은 `Choose`에 들어가며, 확정 월드만 읽고 시뮬레이션에 쓰지 않는다. 선택 중 호버는 표시 전용 비신뢰 RPC(`FusionConnection.Rpc_CardHover`, 규칙은 `CardHoverRelay`)로만 상대에게 보내며 입력·확정 기록·체크섬·리플레이에 넣지 않는다. 이 RPC 때문에 접속 버전 문자열에 `-hover1`이 붙는다. 카드 확정 대기(`PickHoldTicks`)를 144틱으로 늘린 설정(2026-11-14)끼리만 접속하도록 `-hold144`도 붙는다. 선택 단계·선택자·선택지·중복을 검사하고, 클릭 의도는 확정까지 유지하며 라운드 번호로 오래된 요청을 막는다. UI의 `BeginCardFeedback`은 즉시 피드백이고 실제 `PickedAt`·보유 상태와 다르다.

- **능력:** 설정과 보유 카드에서 Simulation이 계산한다. UI 클릭·애니메이션 종료·별도 RPC에서 체력이나 카드를 증감하지 않는다. 타이머·스택·쿨다운·소환물·결과에 관여하는 난수는 복원 가능한 SimState에 둔다.
- **규칙:** 중첩 상한·적용 순서·반올림·상충 효과 우선순위를 먼저 정한다. 경기 RNG의 소비 순서를 보존하고 프레임 시각이나 UI 랜덤으로 판정하지 않는다. 새 밸런스는 사용자와 확정한다.
- **카드 종류:** 고유 카드 ID와 화면 선택지 번호를 구분한다. 현재 배열에는 세 장 전제가 있어 카탈로그와 표시할 세 슬롯을 분리한 뒤 종류를 늘려야 한다. 기존 ID를 다른 효과로 재사용하지 않는다.
- **연출:** FrontendView/Presenter에서 크기·뒤집기·곡선·소리 등을 수정한다. 기존 저장 UI 프리팹을 사용하고 Main을 초기화 도구로 다시 생성하지 않는다. 연출이 길어져 PickHoldTicks나 선택 시점을 바꾸면 경기 규칙 변경이다.
- **검사:** 단일·중첩·상충·초기화·재대결, 카드 요청 중복/시간 초과, 상태 복원·확정 사건 한 번 발생·구 리플레이를 확인한다. 기존 `FusionPredictionTests`, `CardCollectionFlowTests`, UI 검사와 리플레이 검사를 출발점으로 한다.

새 타입은 한 가지 책임과 기존 코드로 해결하지 않은 이유를 기록한다. 카드마다 접속 관리자나 RPC를 만들지 않는다. 입력 규격이 부족하면 명시적인 InputFrame 변경과 리플레이/온라인 버전 검토로 처리한다.

## 시간 진단과 표시

HUD의 `RTT`는 Fusion `GetPlayerRtt` 관측값이다. 이전 `PING`은 제어 메시지 왕복값이었으므로 **이전 숫자와 새 숫자를 직접 비교해 회선이 빨라졌다고 해석하지 않는다.** SDK RTT도 상대 PC 네트워크 처리 시간을 포함하며 ICMP 회선 Ping·입력 지연과 같지 않다. 미측정은 `-`로 표시한다.

`Replays/Network/online_prediction_P*.csv`의 `prediction-v1` 형식은 유지한다. 초당 한 줄, 종료 때 writer를 해제하며 마지막 1초 미만 구간은 별도 행으로 기록하지 않는다. 저장 오류가 경기 판정을 중단하지 않는다.

| 항목 | 의미·주의 |
|---|---|
| `sdk_rtt_ms` / `control_rtt_ms` | SDK RTT / 제어 메시지 RTT. 후자는 상대 처리 대기 보정을 포함하지만 차이를 단순히 CPU·회선 지연으로 분해할 수 없다. |
| `network_hz` / `send_hz` / `delay_ticks` | 적용된 60/60/3 설정. 송신 설정은 실제 UDP 초당 패킷 수와 다르다. |
| `path` | Direct/Relay 접속 경로. 같은 공유기라는 이유만으로 0ms가 되는 것은 아니다. |
| `forward_total` / `resim_total` | 누적 순방향/실제 SDK 재실행 틱. 누적값은 구간 차이로 해석한다. |
| `corrected_ticks_total` | 관측한 같은 틱 비교에서 달라진 상태. 모든 수신 스냅샷의 화면 보정을 포착하는 지표는 아니다. |
| `host_missing_remote_total` | Host가 해당 틱에 상대 입력을 얻지 못한 누적 횟수. 확정 누락과 단순 화면 Ping을 구분한다. |
| `local_age_mean_ms` | 관측한 순방향 입력 표본의 수집→사용 평균. P2의 모든 확정 입력이나 버튼→화면 총 지연이 아니다. |
| `predicted_tick` / `confirmed_tick` / `verified_batches` | 예측 진행·검증한 확정 진행·확정 입력 묶음 수 |
| `frame_mean_ms` / `frame_max_ms` | 해당 기록 구간의 프레임 시간 |

## 기본 접속과 새 PC 설정

개발 환경은 Unity **6000.3.23f1**, SDK는 Fusion **2.1.4 Stable Build 2500**이다. SDK와 `.meta`가 `Assets/Photon`에 포함되어 다른 개발 PC에서 개별 재설치할 필요가 없다. 의존성을 임의로 올리거나 SDK를 덮어 임포트하지 않는다. 원본 출처·라이선스는 [권리 안내](../licenses/ASSET_RIGHTS.md#photon-sdk)를 따른다.

1. 프로젝트를 지정 Unity 버전으로 열고 패키지 복원·임포트·컴파일을 기다린다.
2. `Window > Fusion > Fusion Hub`에서 같은 KnockBox Fusion App ID를 입력한다. 개발 PC마다 새 앱을 만들지 않는다.
3. App ID는 로컬 `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`에 두며 이 파일과 `.meta`는 공유 대상에서 제외한다. 실행 빌드에는 접속용 설정이 포함되므로 빌드만 실행하는 PC는 SDK 설치·App ID 입력이 필요 없다.
4. 양쪽 모두 동일 빌드를 실행한다. 기존 UI의 방 생성 → 코드 전달 → 코드로 참가를 사용하며 이름 입력은 필요하지 않다. 일반 빌드 메뉴는 `Knockbox > Build > Windows Development`다.

RPC/상태 코드 변환에는 공유 `NetworkProjectConfig.fusion`의 `AssembliesToWeave: Knockbox.Game`, Game asmdef의 `allowUnsafeCode: true`, Fusion 심볼·Mono.Cecil 의존성이 필요하다. PlayerSettings의 전역 unsafe만 켜서는 사용자 asmdef에 적용되지 않는다. RPC를 옮길 때 실제 호출 검사도 실행하며 SDK 내부 접근 제한을 수정하지 않는다. `FUSION_LOGLEVEL_NONE` 설정도 유지한다.

Photon 설정과 Unity Cloud 프로젝트·권한·과금, 에디터용 MCP 연결은 서로 별개다. 실제 App ID·인증 정보·방 코드는 진단 문서와 로그에 복사하지 않는다.

## 수정할 때 확인할 곳

| 수정·문제 | 먼저 확인할 코드 | 필요한 확인 |
|---|---|---|
| 방 생성/참가·취소·실패 후 재시도 | OnlineLobby, FusionConnection, FusionRoomCode, ITransportLifetime | 같은 지역/버전, 기존 방만 참가, 취소 소유권, Runner 정리, 상대 이탈 후 복귀 |
| 지연·송신 주기·RTT 표시 | FusionPredictionPolicy, FusionConnection.ReadConnectionTiming, OnlineMatch.LateUpdate/OnGUI, FusionPredictionDiagnostics | SDK 적용 설정과 실제 진단, 입력 누락·재실행·프레임 시간 구분. 숫자만 줄이기 위한 측정 의미 변경 금지 |
| 이동/공격·물리·새 상태 | Simulation, FusionWorldSnapshot, FusionPredictionWorld, FusionPredictionSession | 모든 상태 복원, 스냅샷/접속 버전, 동일 입력 재실행·체크섬·과거 리플레이 |
| 카드 능력·선택·연출 | FrontendInputSource, FusionInputCollector, MatchFlow, FusionConfirmedJournal, FrontendView/Presenter | 선택 의도 유지·단계 검증, 확정 사건 한 번 발생, 시간 초과·중첩·재대결·서로 다른 프레임 속도 |
| 기록 누락·중복 효과·동기화 오류 | FusionConfirmedJournal, MatchRunner.EventWorld/FrontendWorld, LockstepSession | 확정 입력 순서·체크섬, 중복 재적용 방지, 종료 시 확정 구간까지 기록 |

자동 검사의 시작점은 다음과 같다. 상세 선택 기준은 [팀 변경별 검사](../team/TEAM_WORKFLOW.md#변경별-검사)를 따른다.

- `FusionConnectionTests`, `FusionRoomCodeTests`, `FusionTransportLifetimeTests`, `FusionPacketCodecTests`: 접속 정책·수명·RPC 실제 호출·전송 방어 조건.
- `FusionPredictionTests`: 전체 상태 필드 왕복·복원 재실행·확정 기록 검증·카드 의도 유지·카드 흐름과 재대결.
- 기존 `CardCollectionFlowTests`·리플레이 검사, `AuthoredMainTests`·`FrontendFlowTests`: 공통 판정과 UI 회귀.
- `OnlineTimingTests`·`OnlineTimingPlayTests`: 공통 시계·입력 처리 순서. 계정 없는 메모리 전송 검사이며 실제 Photon 두 PC 검증과 구분한다.
- 명시적 `FusionPredictionPlayTests.RealHostClient_ResimulatesAndExportsMatchingAuthoritativeReplay`: 로컬 App ID와 `-onlinebot -netsim 90,3`으로 실제 두 Runner를 실행한다. 900틱의 확정 입력·리플레이 일치, SDK 재실행 발생·확정 사건 중복 방지·종료와 씬 보존을 확인한다. 큰 모의 지연을 주는 검사는 평상시 RTT 품질 측정이 아니다.

실제 두 PC에서는 동일 빌드로 접속·VS/카운트다운·이동/공격·카드 선택·다음 라운드·결과·재대결을 확인한다. 취소·잘못된 코드·상대 이탈 후 복귀·로컬 연습도 확인한다. 측정 파일에는 P1/P2, 유선/Wi-Fi, 같은/다른 인터넷 회선을 구분한다.

현재 3틱의 자동 검사·빌드는 통과했지만 실제 두 PC 3틱 실기와 장시간/다른 회선 검수는 미확인이다. 이전 2/4틱에서 사용자가 밀림을 느끼지 않았다고 보고한 결과를 3틱 검증으로 대체하지 않는다. 정확한 최근 실행 결과는 [PROGRESS](progress/PROGRESS.md), 로그·백업 위치는 [HISTORY](HISTORY.md)에 둔다.

## 문서 갱신 기준

서버 구현을 바꾸면 이 문서의 **실제 정책·호출 경로·책임·버전 계약·미구현 범위**를 코드에 맞춰 갱신한다. 변경할 예정인 기능을 현재 구현으로 쓰지 않는다. 실제 검사 결과는 PROGRESS, 상세 작업 로그·파일 해시·배포 증거는 보관 기록으로 분리한다. 온라인 구조 안내의 사본을 다른 위치에 만들지 않는다.

- [현재 작업 규칙](WORKING_RULES.md), [현행 개발 명세](CURRENT_DEV_SPEC.md), [현재 진행 상태](progress/PROGRESS.md)
- [Photon 시간 설정](https://doc.photonengine.com/fusion/v2/manual/network-project-config), [Runner 수명](https://doc.photonengine.com/fusion/v2/manual/network-runner), [방 생성·참가](https://doc.photonengine.com/fusion/v2/manual/connection-and-matchmaking/matchmaking), [RPC](https://doc.photonengine.com/fusion/v2/manual/data-transfer/rpcs)
