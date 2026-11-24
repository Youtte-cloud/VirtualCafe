using System;
using UnityEditor;
using UnityEngine;

namespace VirtualCafe.Recommendation.Editor
{
    public sealed class CafeSpeechSettingsWindow : EditorWindow
    {
        private CafeSpeechSettings settings;
        private string status = "";
        [MenuItem("Virtual Cafe/Speech Settings")]
        public static void Open() => GetWindow<CafeSpeechSettingsWindow>("Speech Settings");
        private void OnEnable()
        {
            try { settings = CafeSpeechSettings.Load(); }
            catch (Exception) { settings = new CafeSpeechSettings(); status = "Could not load settings. Enter and save again."; }
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("阿里云百炼 / Qwen-ASR 语音识别", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("使用华北2（北京）地域的百炼 API Key。点击麦克风录音，再次点击上传识别，最长30秒。录音发送到阿里云，不保存到磁盘。店员以文字回复。", MessageType.Info);
            EditorGUILayout.LabelField("地域", "华北2（北京）");
            EditorGUILayout.LabelField("模型", CafeSpeechSettings.AliyunModel);
            settings.aliyunApiKey = EditorGUILayout.PasswordField("百炼 API Key", settings.aliyunApiKey);
            if (GUILayout.Button("打开百炼 API Key 管理页面"))
                Application.OpenURL("https://bailian.console.aliyun.com/cn-beijing/model/settings/api-key");
            if (GUILayout.Button("Save on this computer"))
            {
                try { settings.Save(); status = "已保存。返回柜台点击麦克风即可使用；如果正在录音，请先结束当前录音。"; }
                catch (Exception e) { status = e.Message; }
            }
            EditorGUILayout.HelpBox(status.Length == 0 ? "Chinese, English and Japanese are available at the counter." : status, MessageType.None);
            EditorGUILayout.HelpBox("密钥只保存在本机 UserSettings（打包后为应用数据目录），不写入场景或 Assets。也支持环境变量 DASHSCOPE_API_KEY；对话大模型仍使用 LLM Settings。", MessageType.None);
        }
    }
}
