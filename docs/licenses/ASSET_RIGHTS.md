# 리소스 공유 전 확인할 권리

**현재 UI 폰트 3개는 재배포 허용 근거를 확인했습니다.** 폰트와 고지는 함께 보관합니다. LocalOnly 모델과 외부 게임 참고자료는 공개 저장소에 넣지 않습니다.

2026-11-06 확인한 내용입니다. 작업 원칙은 [WORKING_RULES](../ai_docs/WORKING_RULES.md), 리소스 연결 방법은 [아트 안내](../team/ART_WORKFLOW.md)를 봅니다. 아래에서 공식 원문 확인, 로컬 코드 확인, 미확인을 구분합니다.

<a id="1-폰트-파일과-고지를-함께-선택하기"></a>

## 1. 폰트와 고지

다음 파일과 각 `.meta`는 현재 UI에 필요합니다. 이번 이관에서 폰트 내용이나 버전을 바꾸지 않았습니다.


`Assets/_Project/Resources/UI/Paperlogy_4Regular.ttf`

현재 버전: 1.001

함께 둘 고지: [Paperlogy_OFL.txt](Paperlogy_OFL.txt)


`Assets/_Project/Resources/UI/Paperlogy_7Bold.ttf`

현재 버전: 1.001

함께 둘 고지: [Paperlogy_OFL.txt](Paperlogy_OFL.txt)


`Assets/_Project/Resources/UI/HSJiptokki_Black_.ttf`

현재 버전: 1.000

함께 둘 고지: 아래 제작자 조건과 출처를 담은 이 문서


폰트만 빼면 UI 참조가 깨집니다. 반대로 고지를 뒤 커밋으로 미루지도 않습니다. **빌드에는 docs가 자동 포함되지 않으므로**, 게임을 배포할 때는 배포 담당자가 필요한 고지를 함께 제공합니다.

### Paperlogy: SIL OFL 1.1 확인

제작자 페이지와 실제 TTF의 라이선스 메타데이터를 확인했습니다. 수정·임베딩·재배포가 허용됩니다. 글꼴 자체의 단독 판매는 금지되며, 재배포할 때 저작권과 OFL 고지를 보존해야 합니다. 폰트를 수정할 때는 이름 관련 조건도 확인합니다. 이 조건이 게임 전체의 라이선스를 OFL로 바꾼다는 뜻은 아닙니다.

- 저작권: `Copyright © 2024 PT&` / 피티앤.
- 제작 정보: 리디자인 이주임, 기획 김도균. 로컬 메타데이터는 Gmarket Sans와 Montserrat 기반을 명시하고, 공식 페이지는 일본어 M PLUS 2 추가도 설명합니다.
- [제작자 배포 페이지](https://freesentation.blog/paperlogyfont)
- [공식 1.001 배포 ZIP](https://github.com/Freesentation/paperlogy/raw/refs/heads/main/Paperlogy-1.001.zip)
- [SIL 공식 OFL 전문](https://openfontlicense.org/open-font-license-official-text/)

공식 ZIP에는 TTF 9개만 있고 별도 라이선스 텍스트는 없었습니다. 그래서 [Paperlogy_OFL.txt](Paperlogy_OFL.txt)에 실제 폰트의 저작권 고지와 공식 OFL 전문을 함께 보관했습니다.

### HS집토끼체 Black: 제작자 조건 확인

제작자 원문은 상업적 사용과 폰트 파일의 수정·복제·배포를 허용하며, **폰트 파일의 유료 판매는 금지**합니다. 게임 임베딩뿐 아니라 TTF를 무료 공개 저장소에 넣을 근거도 확인했습니다.

원문의 사용범위 표에서 OFL 항목은 X로 표시되어 있습니다. 바로 아래 각주는 이것이 판매 금지를 뜻하며 수정·공유·복제 금지가 아니라고 설명합니다. **이 폰트를 SIL OFL 1.1로 분류하거나 Paperlogy의 OFL을 적용하지 않습니다.**

- 저작권: `Copyright ⓒ Rabbit's Typo Factory All Rights Reserved.`
- 제작: 토끼네활자공장, 최현석 / Hyun S. Choi. 위 정보와 버전 1.000은 실제 TTF에서 확인했습니다.
- [제작자 배포 글](https://blog.naver.com/hp0/222895286224), [직접 확인한 모바일 원문](https://m.blog.naver.com/hp0/222895286224)
- [눈누 보조 안내](https://noonnu.cc/font_page/1092). 최종 근거는 보조 요약표가 아니라 제작자 원문입니다.

**미확인:** 제작자 글에는 2025년·2026년 일부 한글 글리프 수정 안내가 있지만, 현재 파일과 최신 업로드의 동일성은 비교하지 않았습니다. 이번에 업데이트하지 않았습니다. 글자 문제가 생기면 파일과 GUID를 보존하며 별도 변경으로 검수합니다.

<a id="2-box2d-참고-코드-고지-포함하기"></a>

## 2. Box2D 참고 코드 고지

`Assets/_Project/Scripts/Simulation/Systems/PropPhysics.cs`는 Box2D v3의 다각형 클리핑과 접촉 풀이 수식을 이 프로젝트에 맞게 다시 썼다고 주석에 기록합니다. 실제 어느 커밋을 참고했는지는 특정하지 못했습니다. 고지가 빠지지 않도록 [Box2D_MIT.txt](Box2D_MIT.txt)에 출처와 MIT 전문을 보관했습니다.

- [Box2D 공식 저장소](https://github.com/erincatto/box2d)
- 확인한 v3.0.0 원본: [manifold.c](https://github.com/erincatto/box2d/blob/v3.0.0/src/manifold.c), [contact_solver.c](https://github.com/erincatto/box2d/blob/v3.0.0/src/contact_solver.c)
- 두 파일의 표기: `SPDX-FileCopyrightText: 2023 Erin Catto`, `SPDX-License-Identifier: MIT`.

이 고지는 Box2D 패키지를 새로 설치했다는 뜻이 아닙니다. 게임 판정은 기존 Simulation 코드가 담당합니다.

<a id="3-프로젝트에서-만든-자료-구분하기"></a>

## 3. 자체 제작 자료

다음은 **로컬 코드에서 제작 경로를 확인한 자료**입니다. 프로젝트의 모든 권리를 일괄 보증한다는 뜻은 아닙니다.


공용 도형 PNG 8개

제작 경로: `Assets/_Project/Editor/SpriteFactory.cs`

새 작업 시 확인할 점: 여러 곳에서 쓰므로 새 아트로 덮어쓰지 않기


효과음 WAV 17개

제작 경로: `Tools/sfx_synth.py`의 고정 시드 합성

새 작업 시 확인할 점: 현재는 외부 샘플을 읽지 않음. 새 효과음의 출처는 별도 기록


맵 배경·그림자·UI 패널

제작 경로: BackgroundView, ProjectedShadowMesh, FrontendGraphic

새 작업 시 확인할 점: 연출 참고와 외부 파일 복제를 구분


경기 캐릭터·UI 초상

제작 경로: 도형 조합, FighterView/LimbRig, 저장 UI Image

새 작업 시 확인할 점: 외부 3D 시험 모델과 구분


프로젝트 전체의 공개 소스 라이선스는 아직 정하지 않았습니다. 팀이 별도로 결정하며, 폰트·Box2D의 개별 고지는 그 결정과 관계없이 유지합니다.

<a id="4-공개-저장소에서-제외한-자료-확인하기"></a>

## 4. 공유 제외 자료

아래 자료는 삭제하지 않았습니다. 원본 위치와 백업에 남겨 두며, Git만으로 복구된다고 가정하지 않습니다.


`Assets/_Project/LocalOnly/`의 모델·설정

제외 이유: Gang Beast Rigged 업로더의 CC BY 표기는 있으나 원작 게임 캐릭터 권리는 미확인. 폴더의 SOURCE.md도 내부 시험 전용으로 명시


기존 incoming의 GangBeast_Test

제외 이유: 위 시험 모델의 외부 원본


기존 incoming의 UI 참고 이미지·미사용 폰트 굵기

제외 이유: 게임 실행에 필요한 자료가 아님. 사용 중인 세 폰트는 위 조건을 따로 확인


`References/rounds/` 등 외부 게임 캡처·영상

제외 이유: 연출 참고용이며 재배포용 게임 에셋이 아님


개인 Recordings·Captures·빌드·로그

제외 이유: 개인 검수·재현 자료. 로그에는 참가 코드 같은 세션 정보가 포함될 수 있음


LocalOnly 외형은 `MatchPresenter.CreateTestModel()`이 에디터에서만 읽는 선택 기능입니다. 설정이 없거나 사용하지 않으면 기존 2D 캐릭터가 유지됩니다. LocalOnly를 빼더라도 공유하는 FighterModel 관련 코드를 임의 삭제하지 않습니다.

Unity 패키지는 패키지 자체의 라이선스와 고지를 따릅니다. 캐시 대신 `Packages/manifest.json`과 `packages-lock.json`으로 의존성을 보존합니다. 상용 에셋을 새로 도입하면 구매자·팀 이용 범위·소스 파일 재배포 조건을 확인합니다.

## 5. 새 외부 자료를 추가할 때 남길 정보

1. 이름·제작자·원본 URL·받은 날짜와 버전을 적습니다.
2. 라이선스 원문을 보관하고, 수정 여부를 적습니다.
3. 게임에 넣어 배포해도 되는지와 **원본 파일을 공개 팀 저장소에 올려도 되는지**를 따로 확인합니다.
4. 필요한 크레딧과 고지를 자료와 함께 추가합니다.

무료 다운로드라는 사실만으로 재배포가 허용되는 것은 아닙니다. 확인하지 못한 조건은 미확인으로 남기고 개발자에게 전달합니다.
