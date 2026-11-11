using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// 체크섬이 상태의 모든 필드를 덮는지 반사로 확인한다(시뮬레이션은 반사를 쓰지 않고, 테스트만 쓴다).
    /// 상태에 필드를 추가하고 Visit에 빠뜨리면 여기서 실패한다.
    /// </summary>
    public class StateChecksumTests
    {
        static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static bool IsLeaf(Type t) => t.IsPrimitive || t.IsEnum;

        /// <summary>박싱된 값의 말단 필드를 하나 바꾸는 함수 목록.</summary>
        static IEnumerable<(string path, Func<object, object> mutate)> Leaves(Type type, string prefix)
        {
            foreach (var field in type.GetFields(InstanceFields))
            {
                string path = prefix + field.Name;
                if (IsLeaf(field.FieldType))
                {
                    var f = field;
                    yield return (path, boxed =>
                    {
                        f.SetValue(boxed, Change(f.GetValue(boxed)));
                        return boxed;
                    });
                }
                else if (field.FieldType.IsValueType)
                {
                    var f = field;
                    foreach (var (childPath, childMutate) in Leaves(field.FieldType, path + "."))
                    {
                        yield return (childPath, boxed =>
                        {
                            object child = f.GetValue(boxed);
                            f.SetValue(boxed, childMutate(child));
                            return boxed;
                        });
                    }
                }
                else
                {
                    throw new InvalidOperationException($"{path}: 상태에 참조 형식 필드가 있음({field.FieldType.Name})");
                }
            }
        }

        static object Change(object value)
        {
            switch (value)
            {
                case float f: return f + 1.5f;
                case int i: return i + 1;
                case uint u: return u + 1u;
                case sbyte s: return (sbyte)(s == sbyte.MaxValue ? 0 : s + 1);
                case byte b: return (byte)(b + 1);
                case bool flag: return !flag;
                case Enum e:
                    var underlying = Convert.ToInt64(e);
                    return Enum.ToObject(e.GetType(), underlying + 1);
                default: throw new NotSupportedException(value.GetType().Name);
            }
        }

        static bool StateLeafIsActive(string path, bool weaponsEnabled, bool throwEnabled, bool graphEnabled)
        {
            if(path.StartsWith("Props.Graph.",StringComparison.Ordinal))return graphEnabled;
            if (!path.StartsWith("Weapon.", StringComparison.Ordinal)) return true;
            if (!weaponsEnabled) return false;
            // v7에는 투척 확장이 없었다. v8 사례는 어떤 말단 필드도 제외하지 않는다.
            return throwEnabled || !(path == "Weapon.ThrowEnabled" || path == "Weapon.Thrower"
                || path == "Weapon.ThrowHit" || path.StartsWith("Weapon.Velocity.", StringComparison.Ordinal));
        }

        static SimState StateForChecksum(bool weaponsEnabled, bool throwEnabled = false, bool graphEnabled = false)
        {
            var state = TestWorlds.Create().State;
            state.Props.Graph.Enabled=graphEnabled;
            state.Weapon.Enabled = weaponsEnabled;
            state.Weapon.ThrowEnabled = throwEnabled;
            return state;
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        public void Checksum_ChangesWhenAnyStateFieldChanges(bool weaponsEnabled, bool throwEnabled, bool graphEnabled)
        {
            var baseState = StateForChecksum(weaponsEnabled, throwEnabled, graphEnabled);
            uint baseChecksum = baseState.ComputeChecksum();
            var missing = new StringBuilder();
            int count = 0;

            foreach (var (path, mutate) in Leaves(typeof(SimState), ""))
            {
                // 구기록에는 무기 블록 자체가 없다. 활성 사례에서는 새 필드를 하나도 제외하지 않는다.
                if (!StateLeafIsActive(path, weaponsEnabled, throwEnabled, graphEnabled))
                    continue;
                count++;
                object boxed = baseState;
                var changed = (SimState)mutate(boxed);
                if (changed.ComputeChecksum() == baseChecksum)
                    missing.AppendLine(path);
            }

            Assert.Greater(count, 10);
            Assert.AreEqual(string.Empty, missing.ToString(), "체크섬에 빠진 필드");
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        public void Visit_ListsEveryLeafFieldOnce(bool weaponsEnabled, bool throwEnabled, bool graphEnabled)
        {
            int leafCount = 0;
            foreach (var (path, _) in Leaves(typeof(SimState), ""))
                if (StateLeafIsActive(path, weaponsEnabled, throwEnabled, graphEnabled))
                    leafCount++;
            var recorded = StateDiff.Record(StateForChecksum(weaponsEnabled, throwEnabled, graphEnabled));
            // 필드가 없어진 폼 시대 자리도 기록 호환을 위해 고정값으로 해시에 남는다. 목록에 적은 것만 허용한다.
            foreach (string slot in LegacyHashSlots)
                CollectionAssert.Contains(recorded.Names, slot, "기록 호환용 고정값 자리가 해시에서 빠짐");
            Assert.AreEqual(leafCount + LegacyHashSlots.Length, recorded.Names.Count, string.Join(", ", recorded.Names));
            Assert.AreEqual(recorded.Names.Count, new HashSet<string>(recorded.Names).Count, "이름이 겹치는 필드가 있음");
        }

        /// <summary>2026-11-11 폼 전투 제거로 필드는 지웠지만 기존 기록의 체크섬을 지키려고 고정값을 해시에 남긴 자리(2단계에서 제거).</summary>
        static readonly string[] LegacyHashSlots =
        {
            "Fighter0.ParryTicks", "Fighter0.Form", "Fighter0.OwnedForms", "Fighter0.EnergyRegenDelay", "Fighter0.ActionPaid", "Fighter0.ShotPending", "Fighter0.ShotDirection.X", "Fighter0.ShotDirection.Y",
            "Fighter1.ParryTicks", "Fighter1.Form", "Fighter1.OwnedForms", "Fighter1.EnergyRegenDelay", "Fighter1.ActionPaid", "Fighter1.ShotPending", "Fighter1.ShotDirection.X", "Fighter1.ShotDirection.Y",
            "Match.FormChoice0", "Match.FormChoice1",
        };

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public void ConfigChecksum_ChangesWhenAnyParamChanges(bool weaponsEnabled, bool throwEnabled, bool smoothBoxes)
        {
            uint baseChecksum = SimConfig.CreateDefault().ComputeChecksum();
            var missing = new StringBuilder();
            foreach (var (path, mutate) in Leaves(typeof(MovementParams), "Movement."))
            {
                var m = (MovementParams)mutate(MovementParams.Default);
                if (new SimConfig(m, MatchParams.Default).ComputeChecksum() == baseChecksum)
                    missing.AppendLine(path);
            }
            foreach (var (path, mutate) in Leaves(typeof(MatchParams), "Match."))
            {
                var m = (MatchParams)mutate(MatchParams.Default);
                if (new SimConfig(MovementParams.Default, m).ComputeChecksum() == baseChecksum)
                    missing.AppendLine(path);
            }
            // 레거시 생성자는 일부 값(Swing.ForwardOnly)을 고정하므로 규칙 생성자로 확인한다(CP20).
            var rules = RuleSet.Brawl;
            var combat = CombatParams.Default;
            combat.Props.SmoothBoxRelease = smoothBoxes;
            combat.Props.ReleasedBoxFrictionScale = .15f;
            combat.Weapons = weaponsEnabled ? BrawlWeaponParams.Default : default;
            if (throwEnabled)
                combat.Weapons = BrawlWeaponParams.WithThrowing(combat.Weapons);
            // Prepare의 방향 정규화 때문에 축 방향 X만 바꿔 같은 방향이 되는 무의미한 변이를 피한다.
            if (weaponsEnabled)
                combat.Weapons.Pistol.KnockbackDirection = new Vec2(0.8f, 0.6f);
            uint combatBase = new SimConfig(MovementParams.Default, MatchParams.Default, combat, rules).ComputeChecksum();
            foreach (var (path, mutate) in Leaves(typeof(CombatParams), "Combat."))
            {
                if (!weaponsEnabled && path.StartsWith("Combat.Weapons.", StringComparison.Ordinal) && path != "Combat.Weapons.Enabled")
                    continue;
                if (!throwEnabled && path.StartsWith("Combat.Weapons.Throw", StringComparison.Ordinal) && path != "Combat.Weapons.ThrowEnabled")
                    continue;
                var c = (CombatParams)mutate(combat);
                if(!smoothBoxes && path=="Combat.Props.ReleasedBoxFrictionScale")
                {
                    // The old replay path has no release ratio. Assert that contract;
                    // the three enabled cases above still require this field in the hash.
                    Assert.AreEqual(combatBase,new SimConfig(MovementParams.Default,MatchParams.Default,c,rules).ComputeChecksum(),
                        "Inactive release ratio must preserve the legacy config hash");
                    continue;
                }
                if (new SimConfig(MovementParams.Default, MatchParams.Default, c, rules).ComputeChecksum() == combatBase)
                    missing.AppendLine(path);
            }
            Assert.AreEqual(string.Empty, missing.ToString(), "설정 체크섬에 빠진 필드");
        }

        [Test]
        public void DisabledWeaponState_PreservesTheLegacyChecksumSchema()
        {
            var state = StateForChecksum(false);
            var h = Hasher.Create();
            // CP4까지의 순서와 값만 해시한다. 비활성 무기의 0값도 추가되면 구기록이 깨진다.
            h.Value(nameof(SimState.Tick), state.Tick);
            h.Value(nameof(SimState.Rng), state.Rng.State);
            state.Fighter0.Visit(ref h);
            state.Fighter1.Visit(ref h);
            state.Match.Visit(ref h);
            state.Props.Visit(ref h);
            state.Projectiles.Visit(ref h);
            Assert.AreEqual(h.Hash, state.ComputeChecksum());
            state.Weapon.Enabled = true;
            Assert.AreNotEqual(h.Hash, state.ComputeChecksum(), "활성 무기 상태는 반드시 체크섬에 들어간다");
        }

        [Test]
        public void DisabledThrowState_PreservesTheV7WeaponChecksumSchema()
        {
            var state = StateForChecksum(true);
            var weapon = state.Weapon;
            var h = Hasher.Create();
            h.Value(nameof(SimState.Tick), state.Tick);
            h.Value(nameof(SimState.Rng), state.Rng.State);
            state.Fighter0.Visit(ref h);
            state.Fighter1.Visit(ref h);
            state.Match.Visit(ref h);
            state.Props.Visit(ref h);
            state.Projectiles.Visit(ref h);
            // v7에서 실제 사용한 무기 필드 순서. 투척이 꺼지면 새 bool의 false조차 추가하지 않는다.
            h.AddBool(weapon.Enabled);
            h.AddInt((int)weapon.Kind);
            h.AddInt((int)weapon.Phase);
            h.AddInt(weapon.Owner);
            h.AddInt(weapon.AgeTicks);
            h.AddInt(weapon.PhaseTick);
            h.AddFloat(weapon.Position.X); h.AddFloat(weapon.Position.Y);
            h.AddFloat(weapon.LandingPosition.X); h.AddFloat(weapon.LandingPosition.Y);
            h.AddFloat(weapon.FallStart.X); h.AddFloat(weapon.FallStart.Y);
            h.AddInt(weapon.FallDurationTicks);
            h.AddInt(weapon.Ammo);
            Assert.AreEqual(h.Hash, state.ComputeChecksum());
            state.Weapon.ThrowEnabled = true;
            Assert.AreNotEqual(h.Hash, state.ComputeChecksum());
        }

        [TestCase("", 0x811C9DC5u)]
        [TestCase("a", 0xE40C292Cu)]
        [TestCase("foobar", 0xBF9CF968u)]
        public void Hasher_MatchesFnv1aReferenceVectors(string text, uint expected)
        {
            var h = Hasher.Create();
            foreach (byte b in Encoding.ASCII.GetBytes(text))
                h.AddByte(b);
            Assert.AreEqual(expected, h.Hash);
        }

        [Test]
        public void Hasher_UsesFloatBitPattern()
        {
            var a = Hasher.Create();
            a.AddFloat(0f);
            var b = Hasher.Create();
            b.AddFloat(-0f);
            Assert.AreNotEqual(a.Hash, b.Hash, "+0과 -0은 비트가 다르므로 체크섬도 달라야 함");
        }
    }
}
