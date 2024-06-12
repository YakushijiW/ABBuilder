#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

public class EditorConfirmWindow : EditorWindow
{
    Action<string> onOK, onCancel;
    public string content;

    bool isNormal = true;

    string inputFiledVal;

    private void OnGUI()
    {
        GUILayout.Label(content);
        
        if (!isNormal)
        {
            EditorGUILayout.Space();
            inputFiledVal = GUILayout.TextField(inputFiledVal);
            EditorGUILayout.Space();
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("OK"))
        {
            onOK?.Invoke(inputFiledVal);
            Close();
        }

        GUILayout.Space(50);

        if (GUILayout.Button("Cancel"))
        {
            onCancel?.Invoke(inputFiledVal);
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }
    private void OnDisable()
    {
        inputFiledVal = null;
    }
    public void ShowConfirm(string content, Vector2 size, Action onOK = null, Action onCancel = null, string title = "Tip")
    {
        isNormal = true;

        minSize = new Vector2(400, 60);
        position = new Rect(new Vector2(Screen.currentResolution.width - size.x * .5f, Screen.height) * .5f, size);
        this.content = content;
        this.onOK = (str) => { onOK?.Invoke(); };
        this.onCancel = (str) => { onCancel?.Invoke(); };
        titleContent.text = title;
    }

    public void ShowConfirmInput(string content, Vector2 size, Action<string> onOK = null, Action<string> onCancel = null, string title = "Tip")
    {
        isNormal = false;
        inputFiledVal = "Input Here";
        minSize = new Vector2(400, 60);
        position = new Rect(new Vector2(Screen.currentResolution.width - size.x * .5f, Screen.height) * .5f, size);
        this.content = content;
        this.onOK = (str) => { onOK?.Invoke(str); };
        this.onCancel = (str) => { onCancel?.Invoke(str); };
        titleContent.text = title;
    }
}

#endif