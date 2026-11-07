using Knockbox.Game;
using Knockbox.Game.UI;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    [CustomEditor(typeof(MatchRunner))]
    public sealed class MatchRunnerInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var runner=(MatchRunner)target;
            var config=Application.isPlaying && runner.World!=null ? runner.World.Config
                : runner.PreviewConfig(runner.GetComponent<FrontendController>()!=null);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("실제 적용 설정",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("튜닝 에셋 → Brawl 변환 → 프런트엔드 경기 규칙. 몸 크기는 Brawl에서 ×0.8입니다. 차징·잡기·무기 및 경기 진행의 고정 규칙은 코드에서 관리합니다. 아래는 최종값이며 원본값과 다를 수 있습니다.",MessageType.Info);
            using(new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("몸 크기 (최종)",config.Movement.BodySize);
                EditorGUILayout.IntField("최대 체력",config.Combat.MaxHealth);
                EditorGUILayout.IntField("최소 돌진 충전 틱",config.Combat.Body.MinLungeChargeTicks);
                EditorGUILayout.TextField("설정 체크섬",config.ComputeChecksum().ToString("X8"));
                EditorGUILayout.Toggle("경기 중 튜닝 고정",runner.LiveTuningLocked);
            }
            EditorGUILayout.HelpBox("온라인/녹화/재생 중에는 설정을 고정합니다. 로컬 연습의 튜닝 변경은 다음 라운드에 반영됩니다. 과거 기록은 원래 설정 후보로 별도 해석합니다.",MessageType.None);
        }
    }
}
