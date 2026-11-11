# 미사용 기능 보존

2026-11-09 사용자 요청으로 **현재 새 경기의 무기·총알을 보류**했습니다. 삭제가 아니며 추가 개발하지 않습니다.

## 적용 범위

MatchRunner.PreviewConfig가 새 경기에서 weaponsEnabled를 false로 전달합니다. 로컬·온라인이 같은 설정을 사용하며 프라이팬·권총 생성, 줍기·사용·투척·벽꿍 낙하와 총알은 현재 Brawl 경기에서 발생하지 않습니다. 좌클릭 펀치/차징과 상대 잡기/던지기는 유지합니다.

무기 없는 설정도 기존 설정 체크섬에 반영되므로 무기가 켜진 옛 실행 파일과의 온라인 연결은 같은 설정으로 인정되지 않습니다. 프로토콜 9·기록 형식 v8을 바꾸거나 옛 기대 해시를 덮어쓰지 않습니다.

## 보존 위치

아래 경로는 모두 Assets/_Project 기준이며 .meta·GUID를 유지합니다. 공통 시뮬레이션·기록·씬 참조에 걸쳐 있어 파일을 복제하거나 강제로 이동하지 않고, **현재 실행 스위치와 이 미사용 목록으로 분리**합니다.

[무기 판정](../../Assets/_Project/Scripts/Simulation/Systems/BrawlWeapons.cs)

라운드 생성·소유·공격·투척. 설정은 Data/BrawlWeaponParams.cs, 상태는 State/BrawlWeaponState.cs에 있습니다.

[선분-상자 교차](../../Assets/_Project/Scripts/Simulation/Systems/Projectiles.cs)

무기 판정과 연결부 판정이 함께 쓰는 `SegmentBox`만 남아 있습니다. 바위 폼 투사체 시스템은 2026-11-11 폼 전투와 함께 제거했습니다(아래 "제거한 기능").

[무기 표시](../../Assets/_Project/Scripts/Presentation/Fighter/BrawlWeaponView.cs)

Prefabs/Frontend/WeaponVisual.prefab 및 Main의 저장 참조를 보존합니다. 현재 비활성 상태를 읽어 표시하지 않으며 과거 기록에는 사용할 수 있습니다.

[호환 검사](../../Assets/_Project/Tests/EditMode/BrawlInputCompatibilityTests.cs)

BrawlWeaponTests·BrawlWeaponThrowTests·BrawlWeaponPresentationTests, Tools/ReplayCheck의 기존 기록·기대값도 보존합니다. 신규 무기 기능 개발과 구 동작 회귀 검사는 구분합니다.

## 재활성화 조건

다시 사용하려면 별도 승인 후 현재 맵 형상·입력·온라인·리플레이와의 호환성을 검증합니다. 이번 맵 확장에서 새 다각형용 총알·무기 판정을 추가하지 않습니다. 설정 한 줄만 켜면 모든 새 맵에서 안전하다고 보장하지 않습니다.

## 제거한 기능 — 폼 전투 · 2026-11-11

사용자 결정으로 가위·바위·보 폼 전투(초기 폼 선택, 폼 전환, 에너지, 가드·패링, 바위 투사체, 보 휘두르기, 시험 공격 폼)와 캐릭터 외형 후보(CharacterSample)를 **삭제**했습니다. 보류가 아니라 폐기이며, 폼 시대의 시나리오·입력 기록·기대 체크섬도 보존하지 않습니다.

- **남긴 것:** 몸(Body) 폼의 주먹·차징 박치기·잡기·던지기, 현재 Brawl·카드 흐름, 맵 제작기, 온라인 락스텝, 현재 규칙의 기록(v5~v9 몸 폼 기록 포함)과 읽기 코드.
- **기록 호환용 고정값(1단계):** 기존 몸 폼 기록과 온라인 설정 대조를 지키려고, 체크섬에 들어가던 폼 시대 자리는 같은 값으로 해시에 남겼습니다. 상태의 `Form`(4)·`OwnedForms`(16)·`ParryTicks`·`EnergyRegenDelay`·`ActionPaid`·`ShotPending`·`ShotDirection`·`FormChoice0/1`(−1), 설정의 시작 폼·가드·에너지 스위치, `CombatParams`의 `TestAttack`·`Swing`·`Energy`·`Rock`·가드 수치, 빈 투사체 풀, 증강 카탈로그, 열거형 번호(Body=4, SetPick=7, `ActionState`의 빈 번호 8·10·16·21), 입력 비트 Guard·SwitchForm입니다. 목록은 `StateChecksumTests.LegacyHashSlots`와 각 코드 주석에 있습니다.
- **2단계(별도 승인):** 기록 형식 v10·온라인 프로토콜 10으로 올리면서 위 고정값을 걷어냅니다. 이때 기존 몸 폼 기록과 기준 리플레이는 더 이상 재생할 수 없습니다.
