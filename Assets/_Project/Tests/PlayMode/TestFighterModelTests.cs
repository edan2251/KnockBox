#if UNITY_EDITOR
using System.Collections;
using Knockbox.Game;
using Knockbox.Presentation;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Knockbox.Tests.PlayMode
{
    /// <summary>
    /// CR-004 3D 시험 외형 연결(로컬 전용 모델이 있을 때만, 없으면 건너뜀): Main 씬에서 P1을 외형 프리팹이 대신 그리고,
    /// 외형은 시뮬레이션 위치를 따라가며, 방향 전환은 시뮬레이션이 즉시·외형이 짧게 따라오고, 커서 방향 주먹에 손이 그쪽으로 간다.
    /// 외형은 판정에 영향이 없다(3D 물리 없음).
    /// </summary>
    public class TestFighterModelTests
    {
        sealed class ConstantInput : IInputSource
        {
            public InputFrame Frame;
            public InputFrame Poll(int tick) => Frame;
        }

        [UnityTest]
        public IEnumerator Model_FollowsSim_TurnsShortly_PunchesTowardAim()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<FighterModelSettings>(FighterModelSettings.LocalPath);
            if (settings == null || settings.prefab == null || !settings.use)
                Assert.Ignore("로컬 시험 모델 설정이 없음(LocalOnly)");
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            var rig = Object.FindFirstObjectByType<FighterModelRig>();
            Assert.IsNotNull(rig, "외형 프리팹이 만들어짐");
            Assert.IsEmpty(rig.GetComponentsInChildren<Rigidbody>(), "3D 물리 없음");

            runner.Running = false;
            var config = runner.World.Config;
            // 카운트다운을 지나 싸움 단계로.
            for (int i = 0; i < 200 && runner.World.State.Match.Phase != MatchPhase.Fighting; i++)
            {
                runner.StepOnce();
                yield return null;
            }
            var input = new ConstantInput();
            runner.Player1Input = input;
            void Tick(InputFrame f)
            {
                input.Frame = f;
                runner.StepOnce();
            }

            // 위치: 외형 발 = 판정 상자 바닥.
            Tick(default);
            yield return null;
            var f0 = runner.World.State.Fighter0;
            Assert.AreEqual(f0.Position.X, rig.transform.position.x, 0.05f);
            Assert.AreEqual(f0.Position.Y - config.HalfBody, rig.transform.position.y, 0.05f);

            // 방향 전환: 시뮬레이션은 즉시, 외형은 짧게 따라온다.
            float yawRight = rig.transform.eulerAngles.y;
            Tick(new InputFrame(-1, 0, InputButtons.None));
            yield return null;
            Assert.AreEqual(-1, runner.World.State.Fighter0.Facing, "판정상 방향은 바로 바뀜");
            float midTurn = Mathf.Abs(Mathf.DeltaAngle(yawRight, rig.transform.eulerAngles.y));
            for (int i = 0; i < 20; i++)
            {
                Tick(new InputFrame(-1, 0, InputButtons.None));
                yield return null;
            }
            float turned = Mathf.Abs(Mathf.DeltaAngle(yawRight, rig.transform.eulerAngles.y));
            TestContext.WriteLine($"[CR-004 3D] 몸 돌기: 첫 프레임 {midTurn:0}°, 20틱 뒤 {turned:0}°");
            // 오른쪽·왼쪽 목표 각은 화면 쪽으로 기운 만큼(towardCamera) 가까워 가장 짧은 경로로 180 − 2 × 기울기만큼 돈다(정면을 지남).
            float full = 180f - 2f * rig.towardCamera;
            Assert.AreEqual(full, turned, 3f, "몸이 반대쪽으로 다 돎");
            Assert.Less(midTurn, full * 0.8f, "첫 프레임에는 아직 도는 중(외형만 짧게 따라옴)");

            // 커서 방향 주먹(위): 주먹 팔의 손이 어깨보다 위로.
            for (int i = 0; i < 20; i++)
            {
                Tick(default);
                yield return null;
            }
            var up = AimDirections.Encode(16);
            Tick(new InputFrame(0, 0, InputButtons.Attack, up));
            yield return null;
            for (int i = 0; i < 6; i++)
            {
                Tick(new InputFrame(0, 0, InputButtons.None, up));
                yield return null;
            }
            Assert.AreEqual(ActionState.Attack, runner.World.State.Fighter0.Action);
            bool rightNear = rig.rightUpperArm.position.z < rig.leftUpperArm.position.z;
            var shoulder = rightNear ? rig.rightUpperArm : rig.leftUpperArm;
            var hand = rightNear ? rig.rightHand : rig.leftHand;
            TestContext.WriteLine($"[CR-004 3D] 위 주먹: 어깨 {shoulder.position}, 손 {hand.position}");
            Assert.Greater(hand.position.y, shoulder.position.y + 0.15f, "손이 커서 방향(위)으로");
        }
    }
}
#endif
