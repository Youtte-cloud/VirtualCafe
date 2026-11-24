using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VirtualCafe.Recommendation.Editor
{
    public static class CafeSpeechUiChecks
    {
        [MenuItem("Virtual Cafe/Speech/Check Conversation UI (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Run this check in Play Mode.");
            var ui = UnityEngine.Object.FindObjectOfType<CafeFrontDeskUI>();
            if (!ui || !ui.TerminalCanvas) throw new InvalidOperationException("Wait for the front desk UI to initialize.");
            try
            {
                for (int i = 0; i < CafeSpeechConversation.Languages.Length; i++)
                {
                    ui.ShowWelcome(); Click(ui, "Voice conversation");
                    Check(ui.InteractionState == "languages", "Language selection opens");
                    Click(ui, CafeSpeechConversation.LanguageNames[i]);
                    var speech = ui.GetComponent<CafeSpeechConversation>();
                    Check(speech.Active && speech.LanguageIndex == i, "Selected language applied");
                    Check(!speech.IsBusy && !speech.NeedsConfiguration, "Welcome does not require speech key or synthesis");
                    speech.PreviewChanged.Invoke("Customer live transcript");
                    Check(ui.GetComponentsInChildren<Text>().Any(t => t.text == "Customer live transcript"), "Customer bubble displays preview");
                    Check(ui.GetComponentsInChildren<Text>().Any(t => t.text == "Barista / 店员"), "Robot reply bubble is visible");
                    Check(typeof(CafeSpeechConversation).GetMethod("Speak") == null, "No speech synthesis entry point");
                    ui.EndPlayerInteraction();
                    Check(!speech.Active && !speech.IsRecording && !speech.IsBusy, "Closing cancels voice");
                    Check(!ui.GetComponentsInChildren<Text>().Any(t => t.text == "Customer live transcript"), "Closing hides customer bubble");
                }
                Debug.Log("PASS: all three language buttons, customer preview bubble, robot text bubble, no synthesis and close cleanup.");
            }
            finally { ui.EndPlayerInteraction(); }
        }
        private static void Click(CafeFrontDeskUI ui, string prefix)
        {
            var button = ui.TerminalCanvas.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text.StartsWith(prefix));
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }
        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}
