using System;
using Knockbox.Simulation;

namespace Knockbox.Game
{
    /// <summary>
    /// 싸움 봇: 상대 상태를 보고 다가가 공격하고, 가드·물러나기·점프 공격·대시를 섞는다.
    /// 무작위 입력보다 공격·가드·패링·클래시·벽꽝이 훨씬 자주 나와서 전투 결정론과 대칭성을 시험하기 좋다(CP4~CP8 테스트).
    /// 행동 선택은 고정 시드 SimRng라 같은 상태 흐름이면 같은 입력을 낸다.
    /// <see cref="Smart"/>로 만든 봇은 모드에 맞게 공격 버튼을 쓴다: 모드 A는 거리를 보고 충전을 길게 하다 상대 쪽(8방향)으로 떼고,
    /// 모드 B는 상대가 위에 있으면 위 휘두르기, 공중에서 상대가 아래면 내려찍기. F6 상대와 봇 영상이 쓴다.
    /// </summary>
    public struct CombatBot
    {
        SimRng rng;
        int mode;
        int ticksLeft;
        readonly bool grabs;
        readonly bool smart;
        readonly CombatMode combatMode;
        /// <summary>바위 폼(CP20): 거리를 벌리고 수평·대각으로 맞을 때 쏜다.</summary>
        readonly bool rock;
        /// <summary>참이면 매 틱 자기 파이터의 지금 폼으로 공격 방식을 정한다(폼 전환 뒤에도 행동 상태와 난수를 이어간다, CP22).</summary>
        readonly bool followForm;
        int chargeTarget;

        /// <param name="grabs">true면 다가가서 잡고 던지는 행동을 섞는다(CP7). 기본값은 이전 CP 테스트의 입력을 바꾸지 않는다.</param>
        public CombatBot(uint seed, bool grabs = false)
        {
            rng = new SimRng(seed);
            mode = 0;
            ticksLeft = 0;
            this.grabs = grabs;
            smart = false;
            combatMode = CombatMode.Test;
            rock = false;
            followForm = false;
            chargeTarget = 0;
        }

        CombatBot(uint seed, bool grabs, CombatMode combatMode, bool rock, bool followForm = false) : this(seed, grabs)
        {
            smart = true;
            this.combatMode = combatMode;
            this.rock = rock;
            this.followForm = followForm;
        }

        /// <summary>모드에 맞게 공격 버튼을 쓰는 봇(F6 상대, 봇 영상).</summary>
        public static CombatBot Smart(uint seed, bool grabs, CombatMode combatMode) => new CombatBot(seed, grabs, combatMode, false);

        /// <summary>파이터의 폼에 맞게 공격 버튼을 쓰는 봇(CR-003). 가위 = 모드 A 봇, 보 = 모드 B 봇, 바위 = 거리를 두고 쏘는 봇.</summary>
        public static CombatBot ForForm(uint seed, bool grabs, FighterForm form) =>
            new CombatBot(seed, grabs, ModeOf(form), form == FighterForm.Rock);

        /// <summary>파이터의 폼을 매 틱 따라가는 봇(BotInputSource). 전환해도 다시 만들지 않는다.</summary>
        public static CombatBot FollowingForm(uint seed, bool grabs) => new CombatBot(seed, grabs, CombatMode.Test, false, true);

        static CombatMode ModeOf(FighterForm form) =>
            form == FighterForm.Scissors ? CombatMode.Charge : form == FighterForm.Paper ? CombatMode.Swing : CombatMode.Test;

        public InputFrame Next(in FighterState self, in FighterState other, int tick)
        {
            if (ticksLeft-- <= 0)
            {
                mode = rng.Range(0, grabs ? 7 : 6);
                ticksLeft = rng.Range(6, 32);
            }
            float dx = other.Position.X - self.Position.X;
            int toward = dx > 0f ? 1 : dx < 0f ? -1 : self.Facing;
            float distance = dx < 0f ? -dx : dx;
            bool close = distance < 2.2f && (other.Position.Y - self.Position.Y) * (other.Position.Y - self.Position.Y) < 2.5f;
            // 잡기 상자(몸 앞 0.65, 폭 0.7)가 닿는 거리.
            bool grabRange = distance < 1.35f && (other.Position.Y - self.Position.Y) * (other.Position.Y - self.Position.Y) < 0.6f;
            var buttons = InputButtons.None;
            int moveX = toward;
            int moveY = 0;
            switch (mode)
            {
                case 0: // 다가가서 연타(잡기를 쓰는 봇은 가끔 잡기를 섞는다)
                    if (close && tick % 5 == 0)
                        buttons |= InputButtons.Attack;
                    else if (grabs && grabRange && tick % 5 == 2)
                        buttons |= InputButtons.Grab;
                    break;
                case 1: // 가드를 누르고 버틴다(가끔 다시 눌러 패링 구간을 연다)
                    moveX = 0;
                    if (tick % 26 != 0)
                        buttons |= InputButtons.Guard;
                    break;
                case 2: // 물러나기
                    moveX = -toward;
                    break;
                case 3: // 점프해서 공격(점프를 길게 눌러 Box 가운데 기둥도 넘는다)
                    if (tick % 20 < 14)
                        buttons |= InputButtons.Jump;
                    if (close && tick % 4 == 0)
                        buttons |= InputButtons.Attack;
                    break;
                case 4: // 대시로 붙어서 공격
                    if (!close && tick % 19 == 0)
                        buttons |= InputButtons.Dash;
                    if (close && tick % 3 == 0)
                        buttons |= InputButtons.Attack;
                    break;
                case 6: // 다가가서 잡고, 잡았으면 몇 틱 들고 있다가 위나 뒤로 던진다
                    if (self.Action == ActionState.Holding)
                    {
                        if (self.ActionTick < 6)
                            buttons |= InputButtons.Grab;
                        moveX = tick % 2 == 0 ? -self.Facing : 0;
                        moveY = tick % 3 == 0 ? 1 : 0;
                    }
                    else if (grabRange && tick % 3 == 0)
                    {
                        buttons |= InputButtons.Grab;
                    }
                    break;
                default: // 상대 공격 시작을 보고 가드
                    moveX = close ? 0 : toward;
                    if (close && other.Action == ActionState.Attack)
                        buttons |= InputButtons.Guard;
                    else if (close && tick % 7 == 0)
                        buttons |= InputButtons.Attack;
                    break;
            }
            if (smart)
            {
                // 폼 전환(CP21): 상대 폼을 이기는 폼을 가졌으면 자유 상태에서 가끔(틱마다 1/30) 그쪽으로 전환한다.
                var counter = Counter(other.Form);
                if (self.InMovementState && self.Form != counter && Forms.Owns(self.OwnedForms, counter) && rng.Range(0, 30) == 0)
                    buttons |= InputButtons.SwitchForm;
                float dy = other.Position.Y - self.Position.Y;
                bool isRock = followForm ? self.Form == FighterForm.Rock : rock;
                var aimMode = followForm ? ModeOf(self.Form) : combatMode;
                if (isRock)
                    AimRock(self, toward, distance, dy, tick, ref moveX, ref moveY, ref buttons);
                else if (aimMode == CombatMode.Charge)
                    AimCharge(self, toward, distance, dy, tick, ref moveX, ref moveY, ref buttons);
                else if (aimMode == CombatMode.Swing)
                    AimSwing(self, distance, dy, ref moveY, buttons);
            }
            return new InputFrame(moveX, moveY, buttons);
        }

        /// <summary>
        /// 모드 A: 붙어서 연타하는 대신 중간 거리에서 충전을 시작하고, 정한 충전 틱에 닿거나 상대가 바로 앞이면 떼서
        /// 상대 쪽 8방향으로 돌진한다. 충전 틱은 4(최소 돌진)~40(최대 충전 이후)에서 고른다.
        /// </summary>
        void AimCharge(in FighterState self, int toward, float distance, float dy, int tick,
            ref int moveX, ref int moveY, ref InputButtons buttons)
        {
            int aimY = dy > 1.5f ? 1 : dy < -1.5f ? -1 : 0;
            if (self.Action == ActionState.Charge)
            {
                // 상대가 바로 앞까지 오면 조금이라도 충전한 뒤 뗀다.
                bool release = self.ActionTick >= chargeTarget || (distance < 1.6f && self.ActionTick >= 6);
                buttons = release ? buttons & ~InputButtons.Attack : buttons | InputButtons.Attack;
                buttons &= ~(InputButtons.Guard | InputButtons.Grab | InputButtons.Dash);
                moveX = toward;
                moveY = aimY;
                return;
            }
            bool wantsCharge = (buttons & InputButtons.Attack) != 0
                               || ((mode == 0 || mode == 4 || mode == 5) && distance > 2.2f && distance < 9f && tick % 6 == 0);
            if (!wantsCharge || !self.InMovementState)
            {
                buttons &= ~InputButtons.Attack;
                return;
            }
            buttons |= InputButtons.Attack;
            buttons &= ~InputButtons.Guard;
            // 멀수록 길게 충전한다(빠른 돌진이 더 멀리 간다).
            int lo = distance < 2.2f ? 4 : 14;
            chargeTarget = rng.Range(lo, Math.Min(40, lo + (int)(distance * 4f) + 8));
        }

        /// <summary>4.1의 의도된 상성: 바위는 가위를, 가위는 보를, 보는 바위를 이긴다.</summary>
        public static FighterForm Counter(FighterForm form) =>
            form == FighterForm.Scissors ? FighterForm.Rock : form == FighterForm.Rock ? FighterForm.Paper : FighterForm.Scissors;

        /// <summary>
        /// 바위: 너무 가까우면 물러나며 쏘고, 상대가 수평(높이 차 1.2 안)이나 대각선에 있으면 쏜다. 발사 틱의 입력이 방향이므로
        /// 선딜 동안 조준 방향을 누르고 있는다.
        /// </summary>
        void AimRock(in FighterState self, int toward, float distance, float dy, int tick, ref int moveX, ref int moveY, ref InputButtons buttons)
        {
            float ady = dy < 0f ? -dy : dy;
            bool level = ady < 1.2f;
            bool diagonal = !level && (distance - ady < 1.5f && ady - distance < 1.5f) && distance < 10f;
            int aimY = diagonal ? (dy > 0f ? 1 : -1) : 0;
            if (self.Action == ActionState.Shoot)
            {
                buttons &= ~(InputButtons.Attack | InputButtons.Guard | InputButtons.Grab | InputButtons.Dash);
                moveX = toward;
                moveY = aimY;
                return;
            }
            buttons &= ~InputButtons.Attack;
            // 발사 간격은 무작위(틱마다 1/6), 셋 중 하나는 점프하며 쏴서 마주 쏜 투사체끼리만 계속 상쇄되지 않게 한다(CP20 튜닝 1).
            if (self.InMovementState && (level || diagonal) && distance < 16f && rng.Range(0, 6) == 0)
            {
                buttons |= InputButtons.Attack;
                buttons &= ~InputButtons.Guard;
                if (self.Grounded && rng.Range(0, 3) == 0)
                    buttons |= InputButtons.Jump;
                moveX = toward;
                moveY = aimY;
            }
            else if (distance < 2.5f && mode != 3)
            {
                moveX = -toward;
            }
            else if (distance > 7f)
            {
                moveX = toward;
            }
        }

        /// <summary>모드 B: 휘두르는 순간 상대가 머리 위면 위 휘두르기, 공중에서 상대가 아래면 내려찍기.</summary>
        void AimSwing(in FighterState self, float distance, float dy, ref int moveY, InputButtons buttons)
        {
            if ((buttons & InputButtons.Attack) == 0)
                return;
            if (dy > 1f && distance < 3f)
                moveY = 1;
            else if (!self.Grounded && dy < -0.8f && distance < 2.5f)
                moveY = -1;
            else
                moveY = 0;
        }
    }

    /// <summary>
    /// 지금 경기 상태를 읽어 <see cref="CombatBot.Smart"/>가 입력을 정하는 입력원(F6 상대, 봇 영상).
    /// 경기 설정(모드, 잡기)이 바뀌면 같은 시드로 봇을 새로 만든다.
    /// </summary>
    public sealed class BotInputSource : IInputSource
    {
        readonly Func<SimWorld> world;
        readonly int fighter;
        readonly uint seed;
        CombatBot bot;
        bool created;
        bool botGrabs;
        bool jumpHeld;
        int ledgeJumpTicks;

        /// <param name="world">입력을 정할 때 읽을 경기(MatchRunner는 새 경기마다 SimWorld를 바꾸므로 매번 가져온다)</param>
        /// <param name="fighter">이 봇이 조종하는 파이터(0 = P1, 1 = P2)</param>
        public BotInputSource(Func<SimWorld> world, int fighter, uint seed)
        {
            this.world = world;
            this.fighter = fighter;
            this.seed = seed;
        }

        public uint Seed => seed;

        /// <summary>다음 입력부터 처음 상태의 봇으로 시작한다(새 경기).</summary>
        public void Reset()
        {
            created = false;
            jumpHeld = false;
            ledgeJumpTicks = 0;
        }

        public InputFrame Poll(int tick)
        {
            var w = world();
            if (w == null)
                return default;
            var config = w.Config;
            ref readonly var self = ref fighter == 0 ? ref w.State.Fighter0 : ref w.State.Fighter1;
            // 선택 단계(CR-003 개정): 고를 차례면 시드 무작위로 하나를 누른다(누른 틱과 뗀 틱을 번갈아).
            ref readonly var match = ref w.State.Match;
            bool choosing = match.Phase == MatchPhase.FormSelect && (fighter == 0 ? match.FormChoice0 : match.FormChoice1) < 0
                            || match.Phase == MatchPhase.SetPick && match.Picker == fighter && match.PickedAt < 0;
            if (choosing)
            {
                if (match.PhaseTick < 20 || (match.PhaseTick & 1) == 1)
                    return default;
                int count = match.Phase == MatchPhase.FormSelect ? Forms.PlayableCount : match.OptionCount;
                var pickRng = new SimRng(seed ^ (uint)(tick * 2654435761u));
                return new InputFrame(0, 0, MatchFlow.OptionButton(pickRng.Range(0, count)));
            }
            // 봇은 매 틱 자기 파이터의 폼으로 싸운다(CR-003). 폼이 바뀌어도 다시 만들지 않는다(행동 상태와 난수를 이어감, CP22).
            if (!created || botGrabs != config.GrabEnabled)
            {
                bot = CombatBot.FollowingForm(seed, config.GrabEnabled);
                botGrabs = config.GrabEnabled;
                created = true;
            }
            ref readonly var other = ref fighter == 0 ? ref w.State.Fighter1 : ref w.State.Fighter0;
            var input = bot.Next(self, other, tick);
            input = AvoidBoundaryLunge(self, other, w.Arena, input);
            return AvoidSpikes(self, config, w.Arena, input);
        }

        /// <summary>
        /// 가시 피하기(Pit): 걸어가다 앞이 가시 위 낭떠러지면, 건너편에 닿을 만한 발판이 있을 때만 새로 눌러 길게 점프하고
        /// (아직 점프를 누르고 있으면 멈춰서 뗀다) 없으면 가장자리에서 멈춘다.
        /// 가시 위에 서 있으면(면역 중) 가장 가까운 안전한 쪽으로 뛰어 나가고, 가시 위로 떨어지는 중이면 안전한 쪽으로 몸을 틀며
        /// 공중 점프를 새로 누른다. 면역이 끝나면 가시가 다시 아프므로 면역 중에도 가시 위에 머물지 않는다.
        /// 경계 튕김 맵(CR-002): 아래에 아무것도 없는 곳은 아래쪽 경계가 아프므로 가시와 같이 피하고, 옆 경계 쪽으로는 걸어가지 않는다.
        /// </summary>
        InputFrame AvoidSpikes(in FighterState self, SimConfig config, ArenaDef arena, InputFrame input)
        {
            int moveX = input.MoveX;
            var buttons = input.Buttons;
            if (arena.HasBoundary)
            {
                // 옆 경계: 지금 속도로 멈추는 데 드는 거리(반대 입력의 공중 가속 70 기준) + 여유 안이면 반대로 밀어 멈추고,
                // 그 쪽으로 걷거나 대시하지 않는다.
                int side = self.Position.X >= 0f ? 1 : -1;
                float gap = arena.BoundaryHalfWidth - config.HalfBody - System.Math.Abs(self.Position.X);
                float toward = self.Velocity.X * side;
                float stop = toward > 0f ? toward * toward / (2f * 70f) : 0f;
                if (gap < stop + 0.8f && toward > 0f)
                    moveX = -side;
                else if (moveX == side && gap < 1.2f)
                    moveX = 0;
                if ((buttons & InputButtons.Dash) != 0 && moveX == side && gap < 6f)
                    buttons &= ~InputButtons.Dash;
                // 아래가 비어 있는 단방향 발판에서는 아래+점프로 내려가지 않는다(아래쪽 경계로 떨어짐).
                if (self.Grounded && self.GroundBox >= 0 && input.MoveY < 0 && (buttons & InputButtons.Jump) != 0)
                {
                    ref readonly var ground = ref arena.GetBox(self.GroundBox);
                    if (ground.Kind == TerrainKind.OneWay && SurfaceBelow(arena, self.Position.X, ground.MinY - 0.05f) == TerrainKind.Spike)
                        input = new InputFrame(input.MoveX, 0, input.Buttons);
                }
            }
            float foot = self.Position.Y - config.HalfBody;
            bool overSpikes = SurfaceBelow(arena, self.Position.X, foot + 0.05f) == TerrainKind.Spike;
            if (self.Grounded)
            {
                float aheadX = self.Position.X + moveX * (config.HalfBody + 0.6f);
                bool ledge = moveX != 0 && SurfaceBelow(arena, aheadX, foot + 0.05f) == TerrainKind.Spike;
                if (overSpikes)
                {
                    moveX = SafeDirection(arena, self.Position.X, foot + 0.05f, self.Facing);
                }
                else if (ledge && ledgeJumpTicks == 0 && !LandingAhead(arena, self.Position.X, foot, moveX))
                {
                    moveX = 0;
                    ledge = false;
                }
                if ((overSpikes || ledge) && ledgeJumpTicks == 0)
                {
                    if (!jumpHeld)
                    {
                        ledgeJumpTicks = 14;
                    }
                    else
                    {
                        if (ledge)
                            moveX = 0;
                        buttons &= ~InputButtons.Jump;
                    }
                }
            }
            else if (overSpikes && self.Velocity.Y < 0f)
            {
                moveX = SafeDirection(arena, self.Position.X, foot, self.Facing);
                ledgeJumpTicks = 0;
                if (self.AirJumpsLeft > 0 && !jumpHeld)
                    buttons |= InputButtons.Jump;
                else
                    buttons &= ~InputButtons.Jump;
            }
            if (ledgeJumpTicks > 0)
            {
                ledgeJumpTicks--;
                buttons |= InputButtons.Jump;
            }
            jumpHeld = (buttons & InputButtons.Jump) != 0;
            return new InputFrame(moveX, input.MoveY, buttons);
        }

        /// <summary>
        /// 경계 튕김 맵(CR-002): 충전을 떼는 틱에 돌진 방향으로 상대 너머 3유닛(최대 12유닛, 빠른 돌진이 가는 거리) 안에 경계가 있으면,
        /// 상대 쪽 8방향 중 경계까지 여유가 있는 방향으로 바꿔 뗀다(빗나간 돌진이 경계에 꽂히지 않게). 그런 방향이 없으면
        /// 경계까지 가장 먼 방향으로 뗀다.
        /// </summary>
        static InputFrame AvoidBoundaryLunge(in FighterState self, in FighterState other, ArenaDef arena, InputFrame input)
        {
            if (!arena.HasBoundary || self.Action != ActionState.Charge || input.Held(InputButtons.Attack))
                return input;
            var toOther = other.Position - self.Position;
            float reach = SimMath.Min(LungeReach, SimMath.Sqrt(toOther.LengthSquared) + 3f);
            if ((input.MoveX != 0 || input.MoveY != 0) && BoundaryAhead(self.Position, input.MoveX, input.MoveY, arena) > reach)
                return input;
            int bestX = input.MoveX, bestY = input.MoveY;
            float bestDot = float.MinValue, farthest = float.MinValue;
            int farX = input.MoveX, farY = input.MoveY;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0)
                        continue;
                    float ahead = BoundaryAhead(self.Position, x, y, arena);
                    if (ahead > farthest)
                    {
                        farthest = ahead;
                        farX = x;
                        farY = y;
                    }
                    var d = SimMath.Normalize(new Vec2(x, y));
                    float dot = d.X * toOther.X + d.Y * toOther.Y;
                    if (ahead > reach && dot >= 0f && dot > bestDot)
                    {
                        bestDot = dot;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
            return bestDot > float.MinValue ? new InputFrame(bestX, bestY, input.Buttons) : new InputFrame(farX, farY, input.Buttons);
        }

        /// <summary>봇이 보는 가장 긴 돌진 거리(유닛, 최대 충전 34에서 감속해 멈출 때까지).</summary>
        const float LungeReach = 12f;

        /// <summary>위치에서 방향(8방향 입력)으로 경계(몸 반폭 0.5 안쪽)까지의 거리.</summary>
        static float BoundaryAhead(Vec2 position, int moveX, int moveY, ArenaDef arena)
        {
            var d = SimMath.Normalize(new Vec2(moveX, moveY));
            float best = float.MaxValue;
            float bx = arena.BoundaryHalfWidth - 0.5f, by = arena.BoundaryHalfHeight - 0.5f;
            if (d.X > 1e-4f) best = SimMath.Min(best, (bx - position.X) / d.X);
            if (d.X < -1e-4f) best = SimMath.Min(best, (-bx - position.X) / d.X);
            if (d.Y > 1e-4f) best = SimMath.Min(best, (by - position.Y) / d.Y);
            if (d.Y < -1e-4f) best = SimMath.Min(best, (-by - position.Y) / d.Y);
            return best;
        }

        /// <summary>앞쪽(dir) 0.5~6 유닛 안에 점프로 올라설 만한(발보다 2.8 이하로 높은) 가시 아닌 윗면이 있는가.</summary>
        static bool LandingAhead(ArenaDef arena, float x, float foot, int dir)
        {
            float near = x + dir * 0.5f;
            float far = x + dir * 6f;
            float lo = dir > 0 ? near : far;
            float hi = dir > 0 ? far : near;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                if (box.Kind != TerrainKind.Spike && box.MaxY <= foot + 2.8f && box.MaxX >= lo && box.MinX <= hi)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// x 열에서 높이 y 아래로 처음 만나는 지형의 종류. 없으면 Solid(경기장 바닥은 막혀 있다).
        /// 경계 튕김 맵에서 아래에 아무것도 없으면 Spike(아래쪽 경계가 아프다).
        /// </summary>
        static TerrainKind SurfaceBelow(ArenaDef arena, float x, float y)
        {
            float best = float.NegativeInfinity;
            var kind = arena.HasBoundary ? TerrainKind.Spike : TerrainKind.Solid;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                if (box.MinX <= x && x <= box.MaxX && box.MaxY <= y && box.MaxY > best)
                {
                    best = box.MaxY;
                    kind = box.Kind;
                }
            }
            return kind;
        }

        /// <summary>
        /// 아래에 가시가 아닌 윗면이 있고 지형 속이 아닌 가장 가까운 가로 방향(같으면 바라보는 쪽).
        /// 벽 속 열은 아래에 윗면이 없거나 지형 안이라 안전하지 않다(그쪽으로 틀면 벽을 타고 가시로 미끄러진다).
        /// </summary>
        static int SafeDirection(ArenaDef arena, float x, float y, int facing)
        {
            for (float d = 0.5f; d <= 12f; d += 0.5f)
            {
                bool right = IsSafeColumn(arena, x + d, y);
                bool left = IsSafeColumn(arena, x - d, y);
                if (right && left)
                    return facing;
                if (right)
                    return 1;
                if (left)
                    return -1;
            }
            return facing;
        }

        static bool IsSafeColumn(ArenaDef arena, float x, float y)
        {
            float best = float.NegativeInfinity;
            var kind = TerrainKind.Spike;
            for (int i = 0; i < arena.BoxCount; i++)
            {
                ref readonly var box = ref arena.GetBox(i);
                if (box.MinX > x || x > box.MaxX)
                    continue;
                if (box.BlocksAllSides && box.MinY < y && y < box.MaxY)
                    return false;
                if (box.MaxY <= y && box.MaxY > best)
                {
                    best = box.MaxY;
                    kind = box.Kind;
                }
            }
            return best > float.NegativeInfinity && kind != TerrainKind.Spike;
        }
    }
}
