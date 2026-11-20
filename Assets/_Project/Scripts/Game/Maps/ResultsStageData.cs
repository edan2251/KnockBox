using System;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    /// <summary>
    /// 결과 발표 값(2026-11-20, 결과 공간 맵에 저장). 시간은 결과 공간에 들어온 뒤의 단계 틱(60틱 = 1초)이며 두 PC가 같은 값으로 연출한다.
    /// 조작이 풀리는 틱(unlockTick)은 시뮬레이션 설정(MatchParams.ResultsCeremonyTicks)이 되므로 바꾸면 설정 해시·접속도 달라진다.
    /// 숫자가 떨어지는 틱(signDropTick)도 시뮬레이션 설정(MatchParams.ResultsPropReleaseTick)이다. 숫자 발판 배치·질량·시작 속도는 첫 조형 메뉴가
    /// 결과 맵의 물리 상자·로프로 만들어 맵 판정 해시에 들어간다. 나머지(단상 울림·선수 낙하 시간)는 화면 연출만 바꾼다.
    /// </summary>
    [Serializable]
    public sealed class ResultsStageSettings
    {
        [Header("순서(단계 틱, 60 = 1초). 바닥 재구성은 0~54틱")]
        [Tooltip("시상대 아래 울림이 시작되는 틱. 상승 곡선·먼지·흔들림 세기는 아래 '보상 장면 값'(모놀리스와 같은 값)을 쓴다")]
        public int podiumRumbleTick = 60;
        [Tooltip("승자가 하늘에서 떨어지기 시작하는 틱(시상대가 멈춘 뒤)")] public int winnerDropTick = 110;
        [Tooltip("패자가 떨어지기 시작하는 틱")] public int loserDropTick = 115;
        [Tooltip("하늘에서 착지까지 걸리는 틱")] public int fighterFallTicks = 22;
        [Tooltip("낙하 시작 높이(유닛, 착지 자리 위). 화면 위 밖에서 들어온다")] public float fighterDropHeight = 19f;
        [Tooltip("점수 숫자가 떨어지기 시작하는 틱(숫자 발판 물리가 이 틱부터 움직인다, 시뮬레이션 설정)")] public int signDropTick = 146;
        [Tooltip("조작이 풀리고 재대전을 받기 시작하는 틱(시뮬레이션 설정). 글자가 읽을 자리에 온 뒤")] public int unlockTick = 192;

        [Header("매단 세트 점수 배치(유닛, 화면 가운데 0·0, 위 +10). 점수는 언제나 왼쪽 P1, 오른쪽 P2. 숫자 발판은 첫 조형 메뉴로 다시 만들어야 반영된다")]
        [Tooltip("점수 묶음 가운데 x")] public float signCenterX = 0f;
        [Tooltip("점수 글자 바닥선 y(멈춰 매달린 자리). 숫자가 화면 가운데에 걸치고 위로 조금 더 올라간 자리. 시상대에서 두 번 점프로 아래 가로획 윗면(바닥선 + 획 두께)에 닿는다")] public float scoreBaselineY = -1.4f;
        [Tooltip("점수 글자 높이(숫자 안쪽 칸에 선수 몸 0.9가 넉넉히 들어가는 크기)")] public float scoreHeight = 5.2f;
        [Tooltip("숫자와 콜론 사이 간격(글자 높이 3 기준)")] public float colonSpacing = .7f;
        [Tooltip("밧줄 위쪽 고정점 y(화면 위 밖, 화면 위끝은 10). 낮을수록 숫자 흔들림이 빠르고 덜 밀린다")] public float ropeTopY = 12.5f;
        [Tooltip("떨어지기 시작하는 높이(쉬는 자리 위, 유닛). 숫자 전체가 화면 위 밖(위끝 10)에서 기다리는 높이")] public float signDropHeight = 15f;
        [Tooltip("숫자가 떨어지기 시작하는 속도(유닛/초, 아래 방향). 클수록 '훅' 떨어져 줄에 세게 걸린다(판정 값)")] public float signDropSpeed = 30f;

        [Header("숫자 발판(실제 물리, 결과 공간 첫 조형 메뉴가 이 값으로 물리 상자·로프를 만든다)")]
        [Tooltip("숫자 하나의 질량(선수 1). 클수록 밟아도 덜 기울고 걸어 다녀도 덜 흔들린다")] public float digitMass = 6f;
        [Tooltip("콜론 점 하나의 질량(무거우면 두 숫자 안쪽이 끌려 내려가 V자로 기운다)")] public float dotMass = .05f;
        [Tooltip("콜론 점과 양쪽 숫자를 잇는 줄이 곧은 거리보다 긴 비율(1이면 팽팽). 팽팽할수록 점이 두 숫자를 안쪽으로 세게 당긴다")] public float dotRopeSlack = 1.25f;

        public ResultsStageSettings Copy() => (ResultsStageSettings)MemberwiseClone();
    }
}
