namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// 테스트 묶음(CP17-2). 개발 중에는 빠른 묶음(Slow 제외: 카테고리 필터 "!Slow")만 돌리고, CP를 마칠 때 전체를 돌린다.
    /// </summary>
    public static class TestCategories
    {
        /// <summary>한 건에 수십 초 이상 걸리는 탐색·측정.</summary>
        public const string Slow = "Slow";

        /// <summary>CR-004: 옛 전투·성장(폼, 전환, 세트·선택·증강, 투사체, 에너지, 옛 맵 도달성)에만 해당한다. 삭제하지 않고 빠른 묶음에서 뺀다(빠른 묶음 = Slow와 LegacyCombat 모두 제외).</summary>
        public const string LegacyCombat = "LegacyCombat";
    }
}
