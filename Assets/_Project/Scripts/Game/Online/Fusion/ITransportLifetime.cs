namespace Knockbox.Game.Online
{
    /// <summary>전송 계층이 종료를 알릴 수 있을 때 제공한다. 종료 신호가 없는 연결은 세션의 타임아웃으로 처리한다.</summary>
    public interface ITransportLifetime
    {
        bool Ended { get; }
        EndReason Reason { get; }
        string EndMessage { get; }
    }
}
