using System;

namespace Knockbox.Game.Online
{
    /// <summary>
    /// 카드 선택 무대의 호버를 상대 화면에 보여 주기 위한 표시 전용 전송 규칙. 네트워크와 분리한 순수 상태라 검사에서 시간을 직접 넣는다.
    /// 보내는 쪽: 호버가 바뀌면 바로(최소 간격 0.05초), 바뀌지 않아도 0.25초마다 같은 값을 다시 보낸다(손실 복구).
    /// 받는 쪽: 같은 라운드에서 순번이 커진 값만 받고, 0.75초 넘게 소식이 없으면 호버 없음으로 본다.
    /// 호버는 시뮬레이션 입력·상태·확정 기록·체크섬·리플레이에 들어가지 않는다. 확정 결과는 항상 확정 월드를 따른다.
    /// </summary>
    public sealed class CardHoverRelay
    {
        public const double RepeatSeconds = .25, MinIntervalSeconds = .05, StaleSeconds = .75;
        public const int None = -1, MaxOption = 7;

        /// <summary>(라운드, 순번, 선택지 번호 또는 -1)을 실제로 보내는 곳. 온라인에서는 FusionConnection의 비신뢰 RPC다.</summary>
        readonly Action<ushort, ushort, sbyte> send;
        int sentRound = -1, sentOption = int.MinValue;
        ushort sequence;
        double lastSentAt = double.NegativeInfinity;
        int remoteRound = -1, remoteOption = None, remoteSequence = -1;
        double remoteAt = double.NegativeInfinity;

        public int Sent { get; private set; }
        public int Received { get; private set; }
        public int Dropped { get; private set; }

        public CardHoverRelay(Action<ushort, ushort, sbyte> send) => this.send = send;

        /// <summary>내가 고르는 동안 매 프레임 부른다. option은 호버한 선택지 번호(-1 없음).</summary>
        public void Publish(double now, int round, int option)
        {
            if (round < 0 || round > ushort.MaxValue) return;
            option = option < 0 || option > MaxOption ? None : option;
            if (round != sentRound)
            {
                sentRound = round;
                sequence = 0;
                sentOption = int.MinValue;
                lastSentAt = double.NegativeInfinity;
            }
            bool changed = option != sentOption;
            double since = now - lastSentAt;
            // 너무 잦은 변경은 다음 프레임으로 미룬다(매 프레임 다시 부르므로 마지막 값이 곧 나간다).
            if (changed ? since < MinIntervalSeconds : since < RepeatSeconds) return;
            if (sequence == ushort.MaxValue) return; // 한 선택 단계에서 닿을 수 없는 수(초당 20회 × 15초 ≈ 300).
            sequence++;
            sentOption = option;
            lastSentAt = now;
            Sent++;
            send?.Invoke((ushort)round, sequence, (sbyte)option);
        }

        /// <summary>내가 고르는 단계가 끝나면 부른다. 다음 선택 단계는 순번 1부터 다시 보낸다.</summary>
        public void StopPublishing()
        {
            sentRound = -1;
            sentOption = int.MinValue;
            lastSentAt = double.NegativeInfinity;
        }

        /// <summary>상대가 보낸 값. 같은 라운드의 오래된(순번이 작거나 같은) 값은 버린다. 라운드가 바뀌면 새로 받는다.</summary>
        public void Receive(ushort round, ushort sequenceNumber, sbyte option, double now)
        {
            if (round == remoteRound && sequenceNumber <= remoteSequence) { Dropped++; return; }
            remoteRound = round;
            remoteSequence = sequenceNumber;
            remoteOption = option < 0 || option > MaxOption ? None : option;
            remoteAt = now;
            Received++;
        }

        /// <summary>지금 라운드에서 상대가 호버한 선택지 번호. 라운드가 다르거나 오래 소식이 없으면 -1.</summary>
        public int RemoteOption(double now, int round) =>
            round == remoteRound && now - remoteAt <= StaleSeconds ? remoteOption : None;
    }
}
