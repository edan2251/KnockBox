using Knockbox.Game.UI;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    [CustomEditor(typeof(FrontendView))]
    public sealed class FrontendViewInspector : Editor
    {
        FrontendScreen preview=FrontendScreen.Main;
        bool showReferences;
        public override void OnInspectorGUI()
        {
            var view=(FrontendView)target;
            EditorGUILayout.HelpBox("실제 실행에 쓰이는 UI입니다. 아래 화면 열기로 같은 오브젝트를 보며 RectTransform·Text·Image를 수정하세요. Play는 항상 메인에서 시작합니다. 연출 기준값은 다시 캡처하지 마세요.",MessageType.Info);
            using(new EditorGUI.DisabledScope(Application.isPlaying||!view.IsAuthored))
            {
                preview=(FrontendScreen)EditorGUILayout.Popup("편집 화면",(int)preview,new[]{"경기 화면","메인","코드 참가","로비 대기","대결 소개","카드 선택","경기 결과"});
                if(GUILayout.Button("선택한 화면 열기"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(view.gameObject,"화면 편집 열기");
                    view.PreviewScreen(preview);SceneView.RepaintAll();
                }
            }
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("<Theme>k__BackingField"),new GUIContent("화면 테마"));
            serializedObject.ApplyModifiedProperties();
            showReferences=EditorGUILayout.Foldout(showReferences,"씬에 연결된 참조 (고급)");
            if(showReferences)DrawDefaultInspector();
        }
    }
}
