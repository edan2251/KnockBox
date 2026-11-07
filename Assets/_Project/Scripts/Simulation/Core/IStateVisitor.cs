namespace Knockbox.Simulation
{
    /// <summary>
    /// 상태 필드를 명시적인 순서로 하나씩 받는다. 체크섬(Hasher), 상태 비교(StateDiff),
    /// 나중의 직렬화가 모두 같은 필드 목록을 쓰게 하려고 둔다. 반사(reflection)는 쓰지 않는다.
    /// 구현체는 구조체로 만들어 제네릭 제약으로 넘기면 할당이 생기지 않는다.
    /// </summary>
    public interface IStateVisitor
    {
        void Begin(string scope);
        void End();
        void Value(string name, int v);
        void Value(string name, uint v);
        void Value(string name, float v);
        void Value(string name, bool v);
    }
}
