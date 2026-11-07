namespace Knockbox.Presentation
{
    /// <summary>
    /// 현재 폼 표시 방식(10-forms-and-growth.md 5.1). 연출 계층의 옵션이라 시뮬레이션 체크섬에 영향이 없다.
    /// 폼 모양: 가위 = 삼각형(찌르기), 바위 = 원(투사체), 보 = 사각형(휘두르는 판).
    /// </summary>
    public enum FormDisplayMode
    {
        /// <summary>머리 위에 소유자 색 모양을 늘 표시.</summary>
        Always,
        /// <summary>충전·돌진·선딜·공격 중에만 표시.</summary>
        OnAction,
        /// <summary>작은 회색 표시만 늘.</summary>
        Minimal,
    }
}
