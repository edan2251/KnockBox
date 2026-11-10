# 미사용 기능 보존

2026-11-09 사용자 요청으로 **현재 새 경기의 무기·총알을 보류**했습니다. 삭제가 아니며 추가 개발하지 않습니다.

## 적용 범위

MatchRunner.PreviewConfig가 새 경기에서 weaponsEnabled를 false로 전달합니다. 로컬·온라인이 같은 설정을 사용하며 프라이팬·권총 생성, 줍기·사용·투척·벽꿍 낙하와 총알은 현재 Brawl 경기에서 발생하지 않습니다. 좌클릭 펀치/차징과 상대 잡기/던지기는 유지합니다.

무기 없는 설정도 기존 설정 체크섬에 반영되므로 무기가 켜진 옛 실행 파일과의 온라인 연결은 같은 설정으로 인정되지 않습니다. 프로토콜 9·기록 형식 v8을 바꾸거나 옛 기대 해시를 덮어쓰지 않습니다.

## 보존 위치

아래 경로는 모두 Assets/_Project 기준이며 .meta·GUID를 유지합니다. 공통 시뮬레이션·기록·씬 참조에 걸쳐 있어 파일을 복제하거나 강제로 이동하지 않고, **현재 실행 스위치와 이 미사용 목록으로 분리**합니다.

[무기 판정](../../Assets/_Project/Scripts/Simulation/Systems/BrawlWeapons.cs)

라운드 생성·소유·공격·투척. 설정은 Data/BrawlWeaponParams.cs, 상태는 State/BrawlWeaponState.cs에 있습니다.

[투사체 판정](../../Assets/_Project/Scripts/Simulation/Systems/Projectiles.cs)

State/ProjectileState.cs와 함께 보존합니다. 옛 Paper 폼과 리플레이도 사용하는 공유 코드입니다. 옛 디버그 모드까지 제거한 것은 아닙니다.

[무기 표시](../../Assets/_Project/Scripts/Presentation/Fighter/BrawlWeaponView.cs)

Presentation/Fx/ProjectileView.cs, Prefabs/Frontend/WeaponVisual.prefab 및 Main의 저장 참조를 보존합니다. 현재 비활성 상태를 읽어 표시하지 않으며 과거 기록에는 사용할 수 있습니다.

[호환 검사](../../Assets/_Project/Tests/EditMode/BrawlInputCompatibilityTests.cs)

BrawlWeaponTests·BrawlWeaponThrowTests·BrawlWeaponPresentationTests·ProjectileTests, Tools/ReplayCheck의 기존 기록·기대값도 보존합니다. 신규 무기 기능 개발과 구 동작 회귀 검사는 구분합니다.

## 재활성화 조건

다시 사용하려면 별도 승인 후 현재 맵 형상·입력·온라인·리플레이와의 호환성을 검증합니다. 이번 맵 확장에서 새 다각형용 총알·무기 판정을 추가하지 않습니다. 설정 한 줄만 켜면 모든 새 맵에서 안전하다고 보장하지 않습니다.