# 과거 기록 보관과 복구

완료된 계획·작업 로그·캡처는 현재 사용법과 분리한다. 현재 구조는 [개발 명세](CURRENT_DEV_SPEC.md), 실제 최신 상태와 승인 범위는 [PROGRESS](progress/PROGRESS.md)를 기준으로 한다. 아래 과거 지시를 현재 작업 권한으로 재사용하지 않는다.

## 보관 위치

| 자료 | 보관 위치와 목록 |
|---|---|
| 2026-11-13 온라인 문서 위치·용도 정리 직전 | `Tools/out/online-docs-before-20261113/`에 당시 docs 20개 파일과 크기·해시를 보관했다. 제거한 일회성 작업 상세는 이 보관본에만 남기고 현행 구조 문서는 ai_docs에서 관리한다. 3틱 검사 XML·빌드·Player·ZIP·보존 확인 JSON은 `Tools/out/fusion-default3-*`이며 전달본은 `Builds/KnockBox_Fusion_3Ticks_20261113.zip`이다. |
| 2026-11-13 기본 3틱 확정·중복 정리 직전 | `Tools/out/fusion-default3-before-20261113/`에 485개 파일과 크기·해시, 보호 대상 1,386개 해시를 보관했다. 제거한 세 온라인 안내와 긴 PROGRESS 원문도 포함한다. 현행 서버 구조와 확장 계약은 [온라인 구현 기준](ONLINE_MULTIPLAYER.md), 최신 검사 결과는 PROGRESS를 따른다. |
| 2026-11-13 Fusion 기본 예측 시험 | `Tools/out/fusion-prediction-before-20261113/`에 수정 전 447개 파일과 보호 대상 1,561개 해시를 보관한다. 착수 계획은 완료 후 `Tools/out/fusion-prediction-plan-20261113.md`로 복사·해시 대조해 보관했다. 당시 PROGRESS와 확장 안내 원문은 위 3틱 정리 직전 백업에 있다. 현행 계약은 온라인 개발 기준을 따른다. 전체/런타임 검사 XML·로그와 Player·ZIP·보존 확인 JSON은 같은 `Tools/out/`의 `fusion-prediction-*` 파일이다. |
| 2026-11-13 옛 멀티 제거·시험 맵 복원·문서 통합 직전 | 현재 프로젝트의 `Tools/out/legacy-cleanup-before-20261113/`. 892개 파일을 복사·해시 대조한 백업이며 `manifest.json`에 상대 경로·크기·SHA-256이 있다. `docs/`에 완료된 Fusion·맵 제작 계획, Fusion 이전/RPC 수정 로그, 첫 정리·커밋 안내와 기존 PROGRESS 전체가 있다. 시험 맵 편집 4개와 제거 전 코드·패키지도 포함한다. |
| 2026-11-13 실습 씬·템플릿 제거 직전 | `Tools/out/unused-cleanup-before-20261113/`. 삭제 대상 95개와 편집 전 설정·문서 8개, 총 103개 파일. `manifest.json`과 `protected.json`에 당시 보존 근거가 있다. |
| 2026-11-12 Fusion 이전·RPC 수정 | `Tools/out/fusion-before-20261112/`, `Tools/out/fusion-rpc-fix-before-20261112/`. SDK 적용 전 자료와 RPC 설정 수정 전 자료. 검사·빌드 결과 JSON은 같은 `Tools/out/`의 `fusion-*`, `fusion-rpcfix-*` 파일에 있다. |
| 2026-11-07 이전 명세·요청·결정·CP 기록·캡처 | `D:/BrawlGame/Backups/DocsCurrent_20261107_01/History/docs/ai_docs/`. 당시 `before.json`, `history_manifest.csv`, `validation_final.json`과 Snapshot/RestoreCheck로 목록·해시·복원을 대조했다. 이전 경로의 자료이므로 현재 PC에 존재한다고 가정하지 않는다. |
| 2026-11-11 폼 전투·캐릭터 제거 직전 | `D:/KnockBox_Backups/FormRemoval_20261111_222239/`. 당시 1066개 파일을 별도 복원 대조했다. `baseline/`에 몸 폼 결정론 기준선과 재현 자료가 있다. |

`Tools/out` 백업은 Git 제외 로컬 자료다. Git clone만으로 복구되지 않으며 다른 장치 고장에 대비한 원격 백업으로 간주하지 않는다. 과거 D: 경로 역시 새 PC에서 별도로 확보해야 한다. 공유가 필요하면 보관본을 별도 저장소나 외장 장치에 복사하고 해시를 대조한다.

## 복원 방법

1. 필요한 자료의 원문을 보관 위치에서 먼저 확인한다. 현재 작업 파일을 별도 백업한다.
2. `manifest.json`의 상대 경로와 SHA-256을 대조한다. 씬·에셋·코드는 대응 `.meta`와 함께 다룬다.
3. Unity를 닫고 필요한 파일만 복사한다. 전체 과거 스냅샷을 현재 프로젝트 위에 덮어쓰지 않는다. 전체 복원 검사는 빈 폴더에서 수행한다.
4. 과거 맵 정의를 복원하면 당시 등록 목록·기록의 맵 해시와 함께 검토한다. 이전 접속 코드를 복원할 때는 당시 패키지 의존성도 필요하다.
5. 현재 AGENTS.md·CLAUDE.md·작업 규칙을 과거 버전으로 일괄 교체하지 않는다.

백업·개인 로그·빌드·캡처를 공개 Git에 다시 넣기 전에는 [권리와 공유 범위](../licenses/ASSET_RIGHTS.md)를 확인한다. App ID·인증 정보·참가 코드는 작업 기록에 복사하지 않는다.

## 옛 문서 이름과 체크포인트

코드 주석의 `01-architecture.md`, `03-combat.md`, `09-props-physics.md`, `CR-002`~`CR-005`, `08 5절` 등은 이관 전 Project_Brawl의 명세·요청서다. 위 D: 보관본의 `spec/`, `requests/`에서 찾는다. 이관 기록은 `progress/migration_20261106.md`, 끊김 조사는 `progress/practice_perf_20261106.md`다.

코드 주석의 옛 `CP1`~`CP23`과 맵 제작기 CP1~CP5는 다른 작업이다. 맵 제작기 단계는 모두 완료했고 상세 계획은 2026-11-13 백업 안의 docs/ai_docs/MAP_AUTHORING_PLAN.md에 있다. 현재 사용법은 [맵 제작 안내](../team/MAP_AUTHORING.md)를 따른다. 원문이 현재 작업 사본에 없던 `MONOLITH_CARD_PICK_PLAN.md`의 내용·승인을 추정하지 않으며 관련 연출을 구현할 때 사용자 요구를 다시 확인한다.

폼 전투·캐릭터 후보는 사용자 결정으로 폐기했다. 현재 유지하는 몸 폼 기록과 체크섬 자리, 무기 보류 범위는 [미사용 기능 보존](INACTIVE_FEATURES.md)에 있다. 보관본에 파일이 남아 있다는 이유로 폐기한 기능을 다시 활성화하지 않는다.
