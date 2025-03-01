#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using ABBuilder;

namespace ABBuilderEditor
{
    [CustomEditor(typeof(BuilderConfigScriptable))]
    public class BuilderConfigInspector : Editor
    {
        BuilderConfigScriptable instance;
        bool openHeader1 = false;

        private void OnEnable()
        {
            instance = (BuilderConfigScriptable)target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            openHeader1 = EditorGUILayout.BeginFoldoutHeaderGroup(openHeader1, $"AES Encrypt:");
            if (openHeader1)
            {
                EditorGUILayout.LabelField($"EncryptKey = " + ABBuildConfig.BundleEncryptKey);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"EncryptIV = " + ABBuildConfig.BundleEncryptIV);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Change settings at script: ABBuildConfig");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("LoadConfig"))
            {
                LoadConfig();
            }
            EditorGUILayout.EndHorizontal();
        }

        void LoadConfig()
        {
            var backuppath = BuilderConfigScriptable.GetBackupPath();
            if (!File.Exists(backuppath))
            {
                Debug.Log($"Backup at path[{backuppath}] NOT found");
                return;
            }
            var wnd = EditorWindow.GetWindow<EditorConfirmWindow>();
            wnd.minSize = new Vector2(400, 60);
            System.Action onok = () =>
            {
                using (var fs = new FileStream(backuppath, FileMode.Open, FileAccess.Read))
                {
                    var bytes = new byte[fs.Length];
                    fs.Read(bytes, 0, bytes.Length);
                    string content = System.Text.Encoding.UTF8.GetString(bytes);
                    BuilderConfigScriptable val = CreateInstance<BuilderConfigScriptable>();
                    JsonUtility.FromJsonOverwrite(content, val);
                    if (val == null) { Debug.LogError("Parse to BuilderConfigScriptable FAILED"); return; }
                    BuilderConfigScriptable.Save(val);
                    Debug.Log($"Load BuilderConfig backup success");
                }
            };
            wnd.ShowConfirm("Are you sure to load backup config?\n(this operation will REPLACE the current)",
                wnd.minSize, onok, null);
        }
    }
}


#endif