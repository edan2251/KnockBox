# 기존 프로젝트 열기

처음 합류한 팀원은 새 Unity 프로젝트를 만들지 않고 이 저장소를 엽니다. 각 PC에서 저장소를 받은 폴더를 선택합니다. Photon SDK는 저장소에 포함되며 App ID는 각 PC의 로컬 설정입니다.

## 처음 실행하는 순서

1. [작업 규칙](../ai_docs/WORKING_RULES.md)을 읽고, 저장소에 `Assets`, `Packages`, `ProjectSettings`가 있는지 확인합니다. 에셋과 같은 이름의 `.meta`도 함께 받아야 합니다.
2. [ProjectVersion.txt](../../ProjectSettings/ProjectVersion.txt)의 Unity 버전을 확인합니다. 현재는 **6000.3.23f1**입니다. Unity Hub에서 **기존 프로젝트 추가**로 저장소 폴더를 등록합니다.
3. 프로젝트를 열고 패키지 복원과 임포트가 끝날 때까지 기다립니다. [manifest](../../Packages/manifest.json)와 [lock 파일](../../Packages/packages-lock.json)을 기준으로 복원하며, 열기 과정에서 임의로 버전을 올리지 않습니다. Photon SDK는 저장소에서 함께 받으므로 별도로 다운로드·설치하지 않습니다. [Photon 설정](../ai_docs/ONLINE_MULTIPLAYER.md#기본-접속과-새-pc-설정)을 따라 로컬 App ID만 입력합니다.
4. [Main 씬](../../Assets/_Project/Scenes/Main.unity)을 엽니다. Play를 눌러 메인 화면과 연습 모드를 확인합니다. 실제 확인할 항목은 [플레이 검수 안내](PLAYTEST.md)에 있습니다.
5. 맵을 만들 때는 [MapAuthoring 씬](../../Assets/_Project/Scenes/MapAuthoring.unity)을 사용합니다. 저장·등록·시험 순서는 [팀 작업 안내](TEAM_WORKFLOW.md)를 따릅니다.

처음 여는 PC에서는 `Library`가 새로 만들어져 시간이 걸릴 수 있습니다. 이관 당시 PC의 실행 결과가 새 PC에서도 그대로 보장되는 것은 아닙니다. 컴파일 오류가 나면 Unity 버전, 빠진 파일, Console의 첫 오류부터 확인하고 원문을 개발자에게 전달하세요.

## 작업에 필요한 안내

- **게임과 UI 편집:** [팀 작업 안내](TEAM_WORKFLOW.md)
- **그림·폰트·오디오 추가:** [아트 작업 안내](ART_WORKFLOW.md)
- **폴더의 역할과 보존 대상:** [프로젝트 폴더 안내](PROJECT_FOLDERS.md)
- **AI에게 작업 요청:** [AI 참고 안내](../ai_docs/AI_GUIDE.md)
- **커밋 양식:** [공통 커밋 규칙](COMMIT_RULES.md)
- **온라인 설정과 접속:** [Photon Fusion 설정](../ai_docs/ONLINE_MULTIPLAYER.md#기본-접속과-새-pc-설정)

<a id="mcp-없이도-플레이할-수-있나요"></a>

## MCP와 게임 실행

게임 실행에는 MCP 서버가 필요하지 않습니다. MCP는 AI가 열린 Unity 에디터를 확인하거나 조작할 때 사용하는 개발 도구입니다.

현재 설치된 MCP의 설정 창은 서버가 꺼진 동안 상태를 확인하다가 에디터를 잠시 멈추게 할 수 있습니다. 수동 검수 중에는 **MCP 설정 창을 닫고** 플레이하세요. 확인한 원인과 측정 범위는 [성능 조사 기록](../ai_docs/HISTORY.md)에 있습니다. 이 조치는 모든 프레임 끊김의 해결을 보장하지 않습니다.


## 과거 안내

최초 설치·CP24 폼 전투 안내는 [외부 보관본](../ai_docs/HISTORY.md)에 있습니다. 현재 실행 절차나 조작으로 적용하지 않습니다.
