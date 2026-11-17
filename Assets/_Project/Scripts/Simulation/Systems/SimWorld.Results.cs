namespace Knockbox.Simulation
{
    /// <summary>
    /// 점수 없는 자유 공간(2026-11-16 게임 흐름): 경기 뒤의 결과 공간(CP1, MatchEnd)과 온라인 방의 로비(CP3, Lobby 단계).
    /// 두 선수가 자유롭게 움직이고 싸우며 점수·승패는 바뀌지 않고, KO되면 잠시 뒤 자기 자리 위에서 다시 떨어진다.
    /// 떨어지는 동안(착지 전)은 조작이 잠기고 두 선수 사이의 밀어내기·공격 판정을 하지 않는다.
    /// 다음 경기는 준비 비트(ReadyButton)를 계속 보내는 것으로 정한다.
    /// 기존 MatchEnd 규칙(MatchParams.ResultsSpace가 꺼진 설정)은 SimWorld.Step의 원래 경로를 그대로 쓴다.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>
        /// 결과 공간·로비의 준비·재대결 비트. 폼 시대의 SwitchForm 비트를 이 단계에서만 이 뜻으로 쓴다(전투 판정은 이 비트를 읽지 않는다).
        /// 화면은 준비를 켠 동안 이 비트를 계속 보내고, 확정된 PrevButtons로 준비 표시를 읽는다.
        /// </summary>
        public const InputButtons ReadyButton = InputButtons.SwitchForm;

        /// <summary>경기 승자. MatchEnd 동안 마지막 라운드 승자(RoundWinner)가 그대로 남는다.</summary>
        public static int ResultsWinner(in MatchState m) => m.RoundWinner == 1 ? 1 : 0;

        /// <summary>결과 공간 맵의 자리: 승자는 맵의 P1 시작 위치(시상대), 패자는 P2 시작 위치. 슬롯 번호와 관계없다.</summary>
        public static int ResultsSpot(in MatchState m, int fighter) => fighter == ResultsWinner(m) ? 0 : 1;

        /// <summary>착지 전(입장·재등장 낙하) 또는 KO되어 재등장을 기다리는 중.</summary>
        public static bool AwayInResults(in FighterState f) => f.Arriving || f.Action == ActionState.KO;

        /// <summary>MatchEnd에 들어온 틱: 결과 공간 맵으로 옮기고 두 선수를 자기 자리 위에 세운다(낙하는 ResultsDropTick부터).</summary>
        void EnterResultsSpace(ref SimState s)
        {
            s.Projectiles = default;
            PropPhysics.Init(ref s.Props, resultsArena, Config);
            BrawlWeapons.Clear(ref s.Weapon);
            // 승자는 시상대 위에 바로 서고(화면에서 시상대와 함께 솟아오름, CP5), 패자는 자기 자리 위에서 떨어진다.
            int winner = ResultsWinner(s.Match);
            PlaceForDrop(ref s.Fighter0, s.Match, 0, resultsArena, false, winner != 0);
            PlaceForDrop(ref s.Fighter1, s.Match, 1, resultsArena, false, winner != 1);
        }

        /// <param name="lobby">참이면 로비(자리는 슬롯 번호, 준비되면 1라운드), 거짓이면 결과 공간(자리는 승패, 준비되면 새 경기).</param>
        void StepFreeSpace(ref SimState s, in InputFrame p1, in InputFrame p2, ArenaDef arena, bool lobby)
        {
            ref readonly var match = ref Config.Match;
            // 맵이 재구성되는 동안 두 선수는 자리 위에서 기다린다.
            if (s.Match.PhaseTick <= match.ResultsDropTick)
            {
                StepContinuedLobbyFighter(ref s, p1, p2, lobby);
                return;
            }
            Respawn(ref s.Fighter0, s.Match, 0, arena, lobby);
            Respawn(ref s.Fighter1, s.Match, 1, arena, lobby);
            bool interact = !AwayInResults(s.Fighter0) && !AwayInResults(s.Fighter1);
            // 결과 공간은 입장 연출(시상대 상승·승자 점프)이 끝나는 ResultsUnlockTick까지 두 선수 모두 조작을 잠근다(CP5).
            bool locked = !lobby && s.Match.PhaseTick < match.ResultsUnlockTick;
            StepFighters(ref s, locked ? default : ResultsInput(s.Fighter0, p1), locked ? default : ResultsInput(s.Fighter1, p2), interact);
            Land(ref s.Fighter0);
            Land(ref s.Fighter1);
            CheckResultsKnockout(ref s);

            if (s.Match.PhaseTick < match.ResultsUnlockTick)
                return;
            bool ready0 = (p1.Buttons & ReadyButton) != 0;
            bool ready1 = (p2.Buttons & ReadyButton) != 0;
            // 온라인은 둘 다 준비해야, 로컬 연습은 한쪽만 준비해도 새 경기. 로비는 온라인 전용이다.
            if (match.RematchBothHold || lobby ? !(ready0 && ready1) : !(ready0 || ready1))
                return;
            if (lobby)
            {
                // 첫 경기의 맵 순서는 시드로 이미 정해져 있다(CreateMatch와 같은 순환). 난수를 더 쓰지 않는다.
                SimState.StartNewMatch(ref s, Config, arenas[s.Match.Rotation.Current]);
                return;
            }
            if (Config.Rules.LegacyMatch || s.Match.Rotation.Count <= 1)
                s.Match.Rotation.Advance();
            else
                s.Match.Rotation = ArenaRotation.Create(s.Rng.NextUInt(), s.Match.Rotation.Count);
            SimState.StartNewMatch(ref s, Config, arenas[s.Match.Rotation.Current]);
        }

        /// <summary>
        /// 로비 입장 A안(2026-11-17): 방장 혼자 로비에서 이어 간 선수(착지 상태로 시작, <see cref="LobbyCarry"/>)는 공유 첫 틱부터 움직이고,
        /// 낙하를 기다리는 선수는 자리 위에 그대로 둔다. 기존 로비·결과 공간은 이 구간에 두 선수 모두 Arriving이어서 아무것도 하지 않는다
        /// (v4~v9 기록·기대 체크섬 그대로).
        /// </summary>
        void StepContinuedLobbyFighter(ref SimState s, in InputFrame p1, in InputFrame p2, bool lobby)
        {
            if (!lobby || (s.Fighter0.Arriving && s.Fighter1.Arriving))
                return;
            var wait0 = s.Fighter0;
            var wait1 = s.Fighter1;
            StepFighters(ref s, wait0.Arriving ? default : ResultsInput(s.Fighter0, p1), wait1.Arriving ? default : ResultsInput(s.Fighter1, p2), false);
            if (wait0.Arriving) s.Fighter0 = wait0;
            if (wait1.Arriving) s.Fighter1 = wait1;
            CheckResultsKnockout(ref s);
        }

        /// <summary>착지 전과 KO 중에는 조작을 받지 않는다. 준비 비트는 전투 입력에서 뺀다(PrevButtons에는 그대로 남는다).</summary>
        static InputFrame ResultsInput(in FighterState f, in InputFrame input)
        {
            if (AwayInResults(f))
                return default;
            var frame = input;
            frame.Buttons &= ~ReadyButton;
            return frame;
        }

        void PlaceForDrop(ref FighterState f, in MatchState m, int index, ArenaDef arena, bool lobby, bool drop = true)
        {
            int spot = lobby ? index : ResultsSpot(m, index);
            int other = lobby ? 1 - index : ResultsSpot(m, 1 - index);
            SimState.PlaceForDrop(Config, arena, ref f, spot, other, index, drop);
        }

        void Respawn(ref FighterState f, in MatchState m, int index, ArenaDef arena, bool lobby)
        {
            if (f.Action != ActionState.KO || f.HitstopTicks > 0)
                return;
            PlaceForDrop(ref f, m, index, arena, lobby);
        }

        static void Land(ref FighterState f)
        {
            if (f.Arriving && f.Grounded)
                f.Arriving = false;
        }

        /// <summary>체력이 0 이하면 그 선수만 KO. 점수·단계는 바뀌지 않고 히트스탑 동안 멈췄다가 재등장한다.</summary>
        void CheckResultsKnockout(ref SimState s)
        {
            bool down0 = s.Fighter0.Health <= 0 && s.Fighter0.Action != ActionState.KO;
            bool down1 = s.Fighter1.Health <= 0 && s.Fighter1.Action != ActionState.KO;
            if (down0) KnockOutInResults(ref s.Fighter0, ref s.Fighter1, 0, s.Tick);
            if (down1) KnockOutInResults(ref s.Fighter1, ref s.Fighter0, 1, s.Tick);
        }

        void KnockOutInResults(ref FighterState f, ref FighterState other, int index, int tick)
        {
            f.RestartAction(ActionState.KO);
            f.HitstopTicks = Config.Match.ResultsRespawnTicks;
            f.ThrowPending = false;
            Events.Add(new SimEvent(tick, SimEventType.KO, index, f.Position, f.Velocity, Config.Combat.KoHitstop));
            // 붙잡기 중이던 상대는 풀어 준다. 상대도 KO의 순간 멈춤을 함께 받는다.
            if (other.Action == ActionState.Holding || other.Action == ActionState.Held)
                other.RestartAction(other.Grounded ? ActionState.Idle : ActionState.Airborne);
            if (other.Action != ActionState.KO && !other.Arriving)
                other.HitstopTicks = SimMath.Max(other.HitstopTicks, Config.Combat.KoHitstop);
        }
    }
}
