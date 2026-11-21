using System;
using UnityEditor;
using UnityEngine;

namespace VirtualCafe.Recommendation.Editor
{
    public sealed class CafeLlmSettingsWindow : EditorWindow
    {
        private CafeLlmLocalSettings settings;
        private string status = "";
        private bool showKey;

        [MenuItem("Virtual Cafe/LLM Settings")]
        public static void Open() => GetWindow<CafeLlmSettingsWindow>("LLM Settings");

        private void OnEnable() { Reload(); }

        private void Reload()
        {
            try
            {
                settings = CafeLlmLocalSettings.Load();
                status = "Settings loaded. Each teammate saves their own local API key.";
            }
            catch (Exception e)
            {
                settings = new CafeLlmLocalSettings();
                status = "Could not read local settings (" + e.GetType().Name + "). Enter settings and save again.";
            }
        }

        private void OnGUI()
        {
            if (settings == null) Reload();
            EditorGUILayout.LabelField("DeepSeek Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Saved on this computer only, outside Assets. The API key is stored as plain text in a Git-ignored file. Do not share that file.", MessageType.Info);
            settings.endpoint = EditorGUILayout.TextField("Endpoint", settings.endpoint);
            settings.model = EditorGUILayout.TextField("Model", settings.model);
            showKey = EditorGUILayout.Toggle("Show API Key", showKey);
            settings.apiKey = showKey
                ? EditorGUILayout.TextField("API Key", settings.apiKey)
                : EditorGUILayout.PasswordField("API Key", settings.apiKey);

            EditorGUILayout.Space();
            if (GUILayout.Button("Save Local Settings"))
            {
                try
                {
                    settings.Save();
                    status = "Saved. Used by the next request; no Unity restart required. This does not test the API connection.";
                }
                catch (ArgumentException e) { status = e.Message; }
                catch (Exception e) { status = "Save failed (" + e.GetType().Name + ")."; }
            }
            if (GUILayout.Button("Reload Saved Settings")) Reload();
            EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.LabelField("Local file", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(CafeLlmLocalSettings.FilePath, EditorStyles.wordWrappedLabel, GUILayout.Height(50));
        }
    }
}
