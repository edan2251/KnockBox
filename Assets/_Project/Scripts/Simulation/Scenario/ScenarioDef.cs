namespace Knockbox.Simulation
{
    /// <summary>
    /// 시나리오(06-match-and-tools.md 5절)를 파싱한 결과. JSON 읽기는 Game 계층(ScenarioJson)이 한다.
    /// </summary>
    public sealed class ScenarioDef
    {
        public string Name = "";
        public string Arena = "Box";
        public string Mode = "";
        /// <summary>폼 규칙(CR-003): 비어 있으면 mode·grab의 레거시 규칙(가드 켬, 에너지 끔). 있으면 Scissors·Rock·Paper.</summary>
        public string Form = "";
        /// <summary>폼 규칙에서 P2의 시작 폼(비우면 P1과 같음).</summary>
        public string Form2 = "";
        /// <summary>폼 규칙에서 시작 보유 폼 비트(1 가위, 2 바위, 4 보). 0이면 시작 폼만.</summary>
        public int Owned;
        public bool Grab = true;
        /// <summary>폼 규칙일 때만 쓴다. 기본 끔(D-043).</summary>
        public bool Guard;
        /// <summary>폼 규칙일 때만 쓴다. 기본 켬.</summary>
        public bool Energy = true;
        /// <summary>캡처할 때 F3 판정 오버레이를 켠다(연출 전용, 시뮬레이션과 무관).</summary>
        public bool Overlay;
        /// <summary>캡처할 때 `~` 단축키 도움말을 연다(연출 전용).</summary>
        public bool Help;
        /// <summary>화면 테마(0 A 민트, 1 B 사이언, 2 C 슬레이트). -1이면 맵이 정한다(연출 전용, 08 2절).</summary>
        public int Theme = -1;
        public int Ticks;
        public uint Seed = 1;
        public bool HasSpawn;
        public Vec2 Spawn0;
        public Vec2 Spawn1;
        public ScenarioInput[] P1 = new ScenarioInput[0];
        public ScenarioInput[] P2 = new ScenarioInput[0];
        public ScenarioCapture[] Captures = new ScenarioCapture[0];
        public ScenarioExpect[] Expects = new ScenarioExpect[0];
        /// <summary>튜닝 덮어쓰기("Combat.MaxHealth": 30 같은 경로와 값). 적용은 Game 계층(ScenarioConfig)이 한다.</summary>
        public ScenarioTuning[] Tuning = new ScenarioTuning[0];
    }

    /// <summary>튜닝 값 하나의 덮어쓰기. Path는 "Movement.", "Match.", "Combat."으로 시작하는 필드 경로.</summary>
    public struct ScenarioTuning
    {
        public string Path;
        public double Number;
        public bool IsBool;
        public bool Flag;
    }

    /// <summary>방향 입력의 비트 표현. 같은 틱에 좌우가 함께 눌리면 서로 상쇄한다.</summary>
    [System.Flags]
    public enum ScenarioDirections : byte
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
    }

    /// <summary>
    /// 입력 항목. hold: From~To 틱 동안(양 끝 포함) 누르고 있음. press: At 틱부터 HoldFor 틱 동안 누름.
    /// </summary>
    public struct ScenarioInput
    {
        public bool IsPress;
        public int From;
        public int To;
        public int At;
        public int HoldFor;
        public ScenarioDirections Directions;
        public InputButtons Buttons;

        public static ScenarioInput Hold(int from, int to, ScenarioDirections directions, InputButtons buttons) =>
            new ScenarioInput { From = from, To = to, Directions = directions, Buttons = buttons };

        public static ScenarioInput Press(int at, int holdFor, ScenarioDirections directions, InputButtons buttons) =>
            new ScenarioInput { IsPress = true, At = at, HoldFor = holdFor < 1 ? 1 : holdFor, Directions = directions, Buttons = buttons };

        public int FirstTick => IsPress ? At : From;
        public int LastTick => IsPress ? At + HoldFor - 1 : To;
    }

    public struct ScenarioCapture
    {
        public int Tick;
        public string Name;
    }

    public enum ExpectEquality : byte
    {
        None,
        Number,
        Bool,
        Text,
    }

    /// <summary>
    /// 틱 Tick(그 틱까지 Step을 진행한 직후의 상태)에서 Fighter의 Field를 검사한다.
    /// 숫자는 Min/Max 범위, 또는 Equals* 중 하나와 비교한다.
    /// </summary>
    public struct ScenarioExpect
    {
        public int Tick;
        public int Fighter;
        public string Field;
        public bool HasMin;
        public float Min;
        public bool HasMax;
        public float Max;
        public ExpectEquality Equality;
        public float EqualsNumber;
        public bool EqualsBool;
        public string EqualsText;
    }
}
