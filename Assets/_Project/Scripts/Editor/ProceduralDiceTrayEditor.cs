#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Yahtzee.Core;

namespace Yahtzee.Editor
{
    [CustomEditor(typeof(ProceduralDiceTray))]
    public sealed class ProceduralDiceTrayEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            var tray = (ProceduralDiceTray)target;
            using (new EditorGUI.DisabledScope(tray == null))
            {
                if (GUILayout.Button("Rebuild Dice Tray", GUILayout.Height(32)))
                {
                    Undo.RecordObject(tray, "Rebuild Dice Tray");
                    tray.Rebuild();
                    EditorUtility.SetDirty(tray);
                }

                EditorGUILayout.HelpBox(
                    "Portrait: raise Top margin so the tray sits under the score sheet.\n" +
                    "Landscape: raise Right (or Left) margin to leave room for the UI column.\n" +
                    "Tray rebuilds on Play, OnEnable, orientation change, and this button.",
                    MessageType.Info);
            }
        }
    }
}
#endif
