Knockbox 다른 PC 결정론 확인

목적
  같은 빌드와 입력 기록(brawl_sample.kbrp)을 두 번 재생하고, 매 틱(1/60초)의 경기 상태
  체크섬을 reference/trace.txt와 비교한다. Photon 접속이나 다른 플레이어는 필요 없다.
  이 검사는 실제 두 PC 멀티플레이 접속 검사를 대신하지 않는다.

기준 자료와 호환성
  brawl_sample.kbrp와 reference/trace.txt는 과거 기준 자료로 보존한다.
  기록: 1361틱(약 23초), 주먹·박치기·잡기·던지기·벽꽝·벽 기어오르기·KO·라운드 전환 포함.
  현재 소스의 온라인 프로토콜은 9이며 입력 기록은 기존 맵 v8, 제작기 물리 상자·다각형 맵 v9다.
  기록의 설정·맵 체크섬이 맞아야 재생할 수 있다. 모든 과거 전투 규칙의 기록을 지원한다는 뜻은 아니다.
  현재 규칙과 보존 범위는 프로젝트의 docs/ai_docs/CURRENT_DEV_SPEC.md,
  docs/ai_docs/INACTIVE_FEATURES.md를 확인한다. 검증 결과는 docs/ai_docs/progress/PROGRESS.md에 남긴다.

실행 방법
  1. 이 검사 자료와 Knockbox.exe, 실행에 필요한 Data·DLL·Mono 폴더를 함께 준비한다.
     실행 파일만 복사하지 않는다. 다른 PC에서도 같은 빌드와 같은 기준 자료를 사용한다.
  2. 기존 results가 필요하면 먼저 별도로 보관한다. ReplayCheck.bat은 실행 전 results를 비운다.
  3. ReplayCheck.bat을 실행한다. 게임 창 없이 두 번 재생하고 결과를 표시한다.
  4. 결과: PASS이면 해당 실행의 모든 틱이 기준과 일치한 것이다.
     결과: FAIL이면 처음 다른 틱과 앞 틱의 상태 덤프(dump_t틱.txt)를 확인한다.
     결과: ERROR이면 누락 파일·호환되지 않는 기록 등 실행 실패 이유를 확인한다.
  5. 필요한 경우 results 폴더를 압축해 검수 담당자에게 전달한다.
     run1·run2 각각 trace.txt(틱별 체크섬), result.txt(요약), player.log,
     불일치 시 dump_t*.txt가 생긴다. 로그에는 로컬 폴더 경로가 포함될 수 있다.

불일치 조사
  상대 PC의 처음 다른 틱 T에 대해 같은 빌드로 T-1과 T의 덤프를 뽑는다.
    Knockbox.exe -batchmode -nographics -replaycheck brawl_sample.kbrp -dumpticks T-1,T -out mine
  두 PC의 dump_tT.txt를 비교하면 처음 달라진 필드(이름 = 값 [비트])를 찾을 수 있다.
  체크섬은 차이를 감지할 뿐 자동으로 고치지 않는다. 기존 기준값을 새 결과로 덮어쓰지 않는다.

명령줄 인수 (개발 빌드 전용)
  -replaycheck 기록.kbrp   재생할 기록
  -expect trace.txt        비교할 기준 추적
  -dumpticks 10,11         이 틱들의 전체 상태를 dump_t틱.txt로 저장
  -out 폴더               결과 폴더(기본: 실행 파일 옆 results)
  종료 코드: 0 일치, 1 불일치, 2 실행 오류

현재 멀티플레이 접속 방법은 docs/team/PHOTON_SETUP.md를 따른다.
