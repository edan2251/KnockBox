# 아트 작업 안내

아트 파일을 만들고, Unity에 연결하고, 실제 화면에서 확인하는 방법입니다. **2026-11-06 코드·프리팹·임포트 설정**을 확인해 작성했습니다.

작업 범위와 보존·검증 원칙은 [공통 작업 규칙](../ai_docs/WORKING_RULES.md)을 따릅니다. 씬과 편집 화면을 여는 방법은 [Unity 팀 작업 안내](TEAM_WORKFLOW.md)에 있습니다. 여기서는 아트 리소스의 위치, 전달 기준, 개발자와 나눌 일을 설명합니다.

<a id="1-처음에는-작은-작업부터-맡아보세요"></a>

## 1. 첫 아트 작업

첫 작업으로 **카드 아이콘 3개 시안**을 권장합니다. 확정된 작업 지시는 아니며, 시작하기 좋은 범위의 제안입니다.

1. 기존 카드를 덮어쓰지 않고 투명 PNG를 새 파일로 만듭니다.
2. 아트 담당자는 실제 카드 크기에서 가독성, 선택·비선택 명암, 캐릭터와 겹치는 정도를 확인합니다.
3. 개발자가 기존 도형·문자 자식을 새 이미지에 연결합니다.
4. 첫 통합 때 P1과 P2의 카드 선택 화면을 모두 확인합니다.

그다음은 프라이팬·권총 외형, MapAuthoring 테마 색상, 캐릭터 부품과 UI 초상 순서를 권장합니다. 캐릭터 전체 재설계는 팔다리 제어·초상·잔상에 함께 영향을 주므로 첫 작은 작업으로 삼기에는 범위가 큽니다.

<a id="2-바꿀-자료의-위치와-제작-방식을-확인하세요"></a>

## 2. 리소스 위치와 제작 방식

실제 게임 자료는 주로 `Assets/_Project/`에 있습니다. 아래의 `Scenes/`, `Art/`, `Prefabs/`, `Scripts/`, `Data/`, `Resources/`, `Audio/`는 이 폴더 아래 경로입니다. `Tools/`는 프로젝트 루트에 있습니다.

현재 캐릭터는 완성된 그림 한 장을 재생하는 방식이 아닙니다. 작은 도형을 조합하고 코드로 움직입니다. 이미지 교체와 런타임 색상·형태 제어 변경을 구분해야 합니다.

### 경기 캐릭터와 화면 초상

경기 캐릭터는 `Scenes/Main.unity`의 `Fighter_1`, `Fighter_2`입니다. 공용 도형은 `Art/ShapeLibrary.asset`, 표시 코드는 `Scripts/Presentation/Fighter/FighterView.cs`와 `LimbRig.cs`에서 확인합니다.

저장된 몸·외곽선·눈의 SpriteRenderer와 팔다리 부품을 사용합니다. 몸 색, 눈 크기, 포즈, 움직임은 실행 코드도 제어합니다.

화면 초상은 다음 두 프리팹에 UI Image 부품으로 저장되어 있습니다.

- `Prefabs/Frontend/MainAndLobby.prefab`
- `Prefabs/Frontend/DuelCardsResult.prefab`

실제 캐릭터 도형에서 가져온 초상이지만, 경기 캐릭터를 수정해도 저장된 초상이 **자동으로 바뀌지는 않습니다.** 현재 자동 외형 재동기화 도구는 없습니다.

### 메인·로비·VS·카드·결과

- **메인·로비:** `Prefabs/Frontend/MainAndLobby.prefab`의 Text, Image, RectTransform을 편집합니다. 현재 KNOCKBOX 로고는 텍스트입니다. 별도 완성 로고 이미지가 아닙니다.
- **VS·카드·결과:** `Prefabs/Frontend/DuelCardsResult.prefab`을 공유합니다. 캐릭터·패널도 함께 사용합니다. 배경과 명판 일부는 `FrontendGraphic` 메시로 그리며 PNG 한 장으로 되어 있지 않습니다.
- **경기 HUD·보유 카드:** `Prefabs/Frontend/GameplayHud.prefab`, `AcquiredCards.prefab`에서 편집합니다. 플레이어와 준비 상태에 따라 색상이 실행 중 바뀔 수 있습니다.

카드 그림은 `DuelCardsResult.prefab`의 카드 자식에 있습니다. 현재 **한 방**은 팔·손 도형, **맷집**은 캐릭터 부품, **멀리 던지기**는 화살표 문자입니다. 완성 카드 원본 이미지 묶음은 아직 없습니다.

### 맵·배경·그림자

맵 지형과 시작 위치는 `Data/Maps/*.asset`에 저장합니다. `Scenes/MapAuthoring.unity`에서 정의를 편집하며, Main의 생성 지형 Transform을 직접 고치는 방식은 사용하지 않습니다.

배경은 맵 정의의 테마, `Scripts/Presentation/MapThemeSettings.cs`, `Scripts/Presentation/Arena/BackgroundView.cs`가 담당합니다. 그라데이션·미세한 배경 무늬·입자는 코드로 만듭니다. 우선 제작기의 테마 색을 조정하세요. **전체 배경 PNG를 교체하는 슬롯은 현재 없습니다.**

그림자와 점등은 지형 데이터로 만든 투영 메시·셰이더를 사용합니다.

- `Scripts/Presentation/Arena/ProjectedShadowMesh.cs`
- `Resources/Presentation/ProjectedBackground.shader`

셰이더는 문자열 Resources 경로로도 읽습니다. 파일을 누락하거나 임의로 이동하면 안 됩니다.

### 프라이팬·권총

`Prefabs/Frontend/WeaponVisual.prefab`과 `Scripts/Presentation/Fighter/BrawlWeaponView.cs`를 확인합니다. 현재 외형은 원·사각형으로 조립한 임시 모양입니다.

모델과 pan/pistol/face 등의 참조를 유지하면서 새 그림을 연결합니다. 들기·던지기·총구 표현은 실제 공격 판정과 별개입니다. 그림을 바꾸면서 판정까지 함께 바뀐다고 가정하지 마세요.

### 타격·먼지·KO·후처리

타격·먼지·KO는 `Scripts/Presentation/Fx/ImpactFx.cs`, `DustPool.cs`, `KoShatter.cs`와 ShapeLibrary/Palette를 사용합니다. 도형 효과를 풀에서 재사용하며 코드로 제어합니다.

모든 효과가 ParticleSystem 프리팹인 것은 아닙니다. 새 프레임 애니메이션을 추가하려면 개발자의 연결 작업이 필요합니다.

후처리 기본값은 `Prefabs/Frontend/PostFxProfile.asset`에 있습니다. 실행 중 타격 펄스는 복제한 설정에 적용합니다.

### 효과음

현재 효과음은 `Audio/Sfx/*.wav` **17개**입니다. `Tools/sfx_synth.py`의 고정 시드 합성기로 만들었고, `Audio/SfxLibrary.asset`에서 연결합니다.

새 WAV는 별도 이름으로 추가한 뒤 SfxLibrary 슬롯에 연결하세요. 전체 합성기를 실행하면 기존 생성 WAV가 덮어써질 수 있습니다.

### 폰트

실제 폰트는 다음 세 파일입니다.

- `Resources/UI/HSJiptokki_Black_.ttf`
- `Resources/UI/Paperlogy_4Regular.ttf`
- `Resources/UI/Paperlogy_7Bold.ttf`

기존 Text에 연결된 Font 참조가 실제로 사용됩니다. FrontendTheme의 기본 폰트만 바꿔도 저장된 UI가 모두 교체되는 것은 아닙니다. 출처와 재배포 조건은 [권리 안내](../licenses/ASSET_RIGHTS.md)를 확인하세요.

<a id="3-원본과-게임용-파일을-나누어-추가하세요"></a>

## 3. 작업 원본과 게임용 파일

현재 사용 중인 위치는 다음과 같습니다.

- 공용 도형: `Assets/_Project/Art/Sprites/`
- 팔레트·ShapeLibrary: `Assets/_Project/Art/`
- UI·무기 프리팹: `Assets/_Project/Prefabs/Frontend/`
- 폰트: `Assets/_Project/Resources/UI/`

외부 참고 이미지와 예전 원본은 기존 PC의 incoming 등에 있습니다. 새 저장소의 필수 런타임 자료는 아닙니다.

아래는 **새 작업부터 사용할 폴더의 권장안**입니다. 이 안내만으로 모든 폴더를 미리 만들거나 기존 에셋을 옮기지는 않습니다.


편집 원본

권장 위치: `ArtSource/Characters/`, `ArtSource/UI/`, `ArtSource/Environment/`

보관할 내용: PSD·Aseprite·Blender처럼 다시 편집할 수 있는 원본. Unity가 직접 임포트할 필요가 없는 자료


게임용 이미지

권장 위치: `Assets/_Project/Art/Characters/`, `Assets/_Project/Art/UI/`, `Assets/_Project/Art/Environment/`

보관할 내용: 승인된 PNG 등 실행에 사용할 출력물


조립된 게임 오브젝트

권장 위치: 기존 `Assets/_Project/Prefabs/`의 관련 위치

보관할 내용: 실제 실행에 연결한 프리팹. 편집 범위는 담당자와 먼저 맞춤


원본과 게임용 파일은 같은 작업 단위로 전달합니다. 큰 PSD·Blend·오디오 원본은 추가하기 전에 저장소 용량과 LFS 사용 여부를 개발자와 정하세요. 현재 저장소에 LFS가 적용되어 있다고 가정하지 않습니다.

외부 자료에는 출처, 제작자, 사용·재배포 조건, 변경 내용을 함께 남깁니다. 다운로드 파일만 넣지 말고 [권리 안내](../licenses/ASSET_RIGHTS.md)에도 기록합니다.

### 파일 이름과 버전

파일 이름은 공통 규칙을 따릅니다. 예를 들면 `Card_HeavyPunch.png`, `Weapon_Pan.png`, `Character_Body.png`입니다.

게임에 연결한 파일은 가능하면 같은 경로를 유지하고, 수정 이력은 Git으로 남깁니다. 비교 시안은 `Weapon_Pan_OptionA.png`처럼 별개로 만든 뒤 최종 연결 대상을 명확히 표시하세요. 규칙을 맞춘다는 이유로 기존 파일을 일괄 변경하지 않습니다.

<a id="공용-도형은-덮어쓰지-마세요"></a>

### 공용 도형 보존

`square.png`, `circle.png`, `rounded_r12.png` 등은 캐릭터뿐 아니라 지형·UI·무기·효과에서도 공유합니다. `Knockbox/Art/Generate Sprites`가 다시 생성하는 파일이기도 합니다.

새 아트는 **새 경로**에 넣고, 바꾸려는 대상의 Sprite/Image 참조만 연결합니다.

<a id="4-전달할-규격을-함께-적어주세요"></a>

## 4. 전달 규격

아래의 현재 값은 기존 도형에 대한 정보입니다. 모든 새 아트에 일괄 적용할 확정 규격은 아닙니다. 실제 화면·월드 크기와 사용처에 맞춰 정합니다.


투명 배경

현재 확인한 설정: 공용 도형은 알파가 있는 PNG

새 자료에서 확인할 내용: 새 캐릭터·아이콘도 투명 PNG 권장. 흰 테두리 번짐, 잘린 외곽선, 반투명 가장자리 확인


피벗

현재 확인한 설정: 공용 도형은 중앙 `(0.5, 0.5)`

새 자료에서 확인할 내용: 손잡이·총구 위치와 실제 장착 방향 전달. 피벗 변경은 회전·들기 위치에 영향


PPU

현재 확인한 설정: 둥근 몸·외곽선·팔다리·눈 캡슐 200, 원·삼각형·링 128, 사각형 4

새 자료에서 확인할 내용: 모든 새 아트를 200으로 맞추지 않음. 실제 표시 크기를 보고 결정


크기·슬라이스

현재 확인한 설정: 몸·외곽선 96×96, 팔다리 48×24, 눈 캡슐 40×20. 9슬라이스 사용

새 자료에서 확인할 내용: 기존 구조를 그대로 대체할지 먼저 결정. 잘못된 Border는 모서리·눈을 찌그러뜨릴 수 있음


필터

현재 확인한 설정: 사각형은 Point, 나머지 공용 도형은 Bilinear. Mipmap 끔, Clamp, 비압축, Full Rect

새 자료에서 확인할 내용: 기존 설정을 무조건 복제하지 말고 작은 실제 표시 크기로 검수


색상

현재 확인한 설정: 흰 도형에 Palette 색을 곱하는 구조가 많음

새 자료에서 확인할 내용: 단색 마스크인지 완성 컬러인지 표시. 채색 PNG와 런타임 Tint가 겹치면 색이 달라질 수 있음


애니메이션

현재 확인한 설정: 캐릭터는 코드로 포즈·팔다리를 제어

새 자료에서 확인할 내용: 프레임 수·순서·속도·루프 여부·접점 전달. 시트만 넣어도 자동 재생되지는 않음


오디오

현재 확인한 설정: 합성기는 44,100Hz 사용

새 자료에서 확인할 내용: 길이·샘플레이트·모노/스테레오·루프·볼륨 의도 전달. 기존 17개 효과음의 Preload Audio Data 설정 유지


<a id="5-unity에-연결하고-실제-화면에서-확인하세요"></a>

## 5. Unity 연결과 화면 검수

1. Unity 버전을 `ProjectSettings/ProjectVersion.txt`와 맞춥니다. 현재는 **6000.3.23f1**입니다. 메인·맵 씬을 여는 순서는 [팀 작업 안내](TEAM_WORKFLOW.md)를 봅니다.
2. 새 PNG를 정한 Assets 폴더에 넣습니다. Sprite (2D and UI), 피벗·PPU·Border를 확인합니다. Import 설정도 해당 `.meta` 변경으로 저장됩니다.
3. UI는 Main의 `Frontend___edit_these_screens`에서 편집 화면을 열고 실제 Text/Image/RectTransform을 수정합니다. 프리팹 모드에서도 같은 프리팹을 편집합니다. 미리보기 전용 복제본을 따로 납품하지 않습니다.
4. 경기 캐릭터는 Main의 저장된 SpriteRenderer를 확인합니다. ShapeLibrary의 Sprite 변경이 기존 Main 참조를 자동 교체한다고 가정하지 않습니다. 몸·외곽선·눈·팔다리·잔상까지 확인합니다.
5. 캐릭터 형태를 바꿨다면 **메인 → 첫 VS → 카드 선택 → 결과 → 재대결 대기**를 모두 확인합니다. 두 UI 프리팹에 저장된 초상도 별도로 점검합니다.
6. 무기는 **줍기·보유·공격·던지기·바닥 상태**에서 위치와 방향을 확인합니다. 외형 작업에서 판정·사거리·총구의 시뮬레이션 좌표를 임의 변경하지 않습니다.
7. 저장한 뒤 씬을 다시 열고 Play에서 확인합니다. Main 전용 변경인지 공용 프리팹 변경인지 정하고, 필요한 항목만 Apply합니다. Play 중 편집한 값은 종료하면 사라집니다.

<a id="6-개발자에게-변경-내용을-전달하세요"></a>

## 6. 변경 내용 전달


원본·출력물·출처, 목표 화면, 변경 전후 의도

개발자가 함께 확인할 것: 실제 참조와 런타임 색·크기 덮어쓰기


피벗·PPU·픽셀 크기·여백·장착 접점

개발자가 함께 확인할 것: 코드 포즈, 판정과 외형 일치, 여러 해상도


확인한 씬·프리팹과 남은 문제

개발자가 함께 확인할 것: Main·온라인 입력과 상태를 보존한 통합


같은 씬·프리팹은 한 번에 한 사람이 맡는 방식을 권장합니다. 특히 `DuelCardsResult.prefab`은 VS·카드·결과가 한 파일입니다. 서로 다른 화면을 맡아도 충돌할 수 있습니다.

작업 시작 전에 최신 내용을 받고 담당 파일을 공유합니다. Unity 에셋과 `.meta`는 항상 함께 전달하세요. 충돌을 해결하려고 `.meta`를 새로 만들지는 않습니다.

이미지만 바꿨다면 Main이나 프로젝트 설정의 뜻밖의 변경이 함께 섞이지 않았는지 Fork에서 확인합니다.

<a id="7-이전-실험-자료는-구분해서-보세요"></a>

## 7. 이전 실험 자료

`CharacterSample`, `Presentation/Sample`, R1~R4 관련 자료는 옛 외형 실험·검증용입니다. ShapeLibrary, Palette, IK 코드와 테스트를 공유합니다. 현재 화면에 보이지 않는다는 이유로 일괄 삭제하지 않습니다.

`LocalOnly`의 외부 3D 모델과 외부 게임 참고 이미지는 배포용 아트가 아닙니다. 구분과 제외 이유는 [권리 안내](../licenses/ASSET_RIGHTS.md)에 있습니다.
