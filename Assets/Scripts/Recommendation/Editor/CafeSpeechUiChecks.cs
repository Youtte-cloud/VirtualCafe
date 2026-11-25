using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VirtualCafe.Inventory;
using VirtualCafe.Player;

namespace VirtualCafe.Recommendation.Editor
{
    // Exercises actual Play Mode buttons. Never starts recording, submits a model request or confirms an order.
    public static class CafeSpeechUiChecks
    {
        private const string MenuPath = "Virtual Cafe/Speech/Check Conversation UI (Play Mode)";
        private static Stack<IEnumerator> steps;
        private static CafeFrontDeskUI ui;
        private static CafeSpeechConversation speech;
        private static MenuSelection originalSelection;
        private static int originalLanguage, checks;
        private static string originalStock;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            if (steps != null) { Debug.LogWarning("Conversation UI check is already running."); return; }
            if (!Application.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Enter Play Mode and unpause before running this check.");
            ui = UnityEngine.Object.FindObjectOfType<CafeFrontDeskUI>();
            if (!ui || !ui.TerminalCanvas || !EventSystem.current)
                throw new InvalidOperationException("Wait for the front desk UI to initialize.");
            speech = ui.GetComponent<CafeSpeechConversation>();
            if (!speech || !ui.reception || !ui.reception.menuAdapter || !ui.recommendation)
                throw new InvalidOperationException("The front desk references are incomplete.");
            if (ui.IsPreparingOrder || (ui.machineProduction && ui.machineProduction.IsBusy) ||
                ui.recommendation.IsBusy || speech.IsBusy || speech.IsRecording)
                throw new InvalidOperationException("Finish the current request, recording or preparation before checking the UI.");
            if (!ui.IsInReceptionRange)
            {
                Debug.LogWarning("UI check not started: stand near the barista and leave your seat first. The current dialogue has not been closed.");
                return;
            }
            originalLanguage = CafeUiLocalization.LanguageIndex;
            originalSelection = ui.reception.menuAdapter.CurrentSelection;
            originalStock = CafeRuntimeServices.Inventory.GetInventoryJson();
            checks = 0;
            ui.EndPlayerInteraction();
            steps = new Stack<IEnumerator>(); steps.Push(CheckFlow());
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            Debug.Log("Conversation UI check started. It closes the current dialogue and uses a temporary draft. No microphone, API request, purchase or inventory reset will be used.");
        }
        [MenuItem(MenuPath, true)]
        private static bool CanRun() => Application.isPlaying && !EditorApplication.isPaused && !EditorApplication.isCompiling && steps == null;

        private static IEnumerator CheckFlow()
        {
            for (int language = 0; language < CafeSpeechConversation.Languages.Length; language++)
            {
                Debug.Log("Checking conversation UI: " + CafeSpeechConversation.LanguageNames[language]);
                ui.ShowWelcome();
                yield return WaitFor(() => RawButton("中文") || Button("Order by voice"), "Initial language selection or welcome options", false);
                if (!RawButton("中文"))
                {
                    var selector = WindowButtons().FirstOrDefault(b => b.name.EndsWith(" ▾", StringComparison.Ordinal));
                    Check(selector, "Welcome has a compact language selector");
                    Click(selector);
                }
                yield return WaitFor(() => RawButton("中文") && RawButton("English") && RawButton("日本語"), "All three language choices");
                Click(RawButton(CafeSpeechConversation.LanguageNames[language]));
                yield return WaitFor(() => Button("Order by voice"), "Welcome after choosing a language", false);
                Check(CafeUiLocalization.LanguageIndex == language, "UI language follows selection");
                Check(ui.recommendation.ConversationLanguage == CafeSpeechConversation.Languages[language], "LLM language follows selection");
                Check(VisibleText("How would you like to order?"), "Welcome heading is localized");

                // A short fixed greeting must lead to its choices without clicking Continue.
                ui.ShowWelcome();
                Check(!Button("Order by voice"), "Greeting hides the action window");
                Check(ui.GetComponentsInChildren<Text>().Any(t => t.isActiveAndEnabled && t.text == CafeUiLocalization.Text("YOUR BARISTA")), "Bubble speaker label is localized");
                yield return WaitFor(() => Button("Order by voice"), "Short greeting automatically opens choices", false);
                Check(!ui.GetComponentsInChildren<Button>().Any(b => b.isActiveAndEnabled && b.name == "Continue"), "Greeting bubble is dismissed before choices appear");

                Click(Button("Order by voice"));
                yield return WaitFor(() => RawButton(CafeSpeechConversation.LanguageNames[language]), "Voice language choices");
                Click(RawButton(CafeSpeechConversation.LanguageNames[language]));
                Check(speech.Active && speech.LanguageIndex == language, "Voice input uses selected language");
                Check(!speech.IsBusy && !speech.IsRecording && !speech.NeedsConfiguration, "Opening voice dialogue requires no recording or service request");
                yield return WaitFor(() => Button("Speak") && Button("Send"), "Voice preference input");
                Check(ui.InteractionState == "recommendation", "Preference input uses shared recommendation flow");
                var player = UnityEngine.Object.FindObjectOfType<CafeDesktopPlayer>();
                Check(player && player.UiMode && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Action window pauses movement and releases cursor");
                player.SendMessage("OnApplicationFocus", false);
                Check(player.UiMode && Cursor.visible, "Losing focus keeps an open panel usable");
                player.SendMessage("OnApplicationFocus", true);
                Check(player.UiMode && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Regaining focus preserves the panel pointer");
                Check(!Button("Voice settings"), "Customer panel has no credential settings entry");

                var field = ui.TerminalCanvas.GetComponentsInChildren<InputField>().Single(f => f.isActiveAndEnabled);
                field.text = "UI check draft";
                speech.PreviewChanged.Invoke("Speech recognition failed: simulated status");
                yield return null;
                Check(field.text == "UI check draft", "Speech errors never overwrite the input draft");
                Check(!ui.recommendation.IsBusy, "Speech status never submits an LLM request");
                Check(Cursor.visible && player.UiMode, "Speech status does not hide the cursor");
                speech.Recognized.Invoke("   ");
                yield return null;
                Check(field.text == "UI check draft" && !ui.recommendation.IsBusy, "Empty recognized text is ignored");
                Click(Button("Retry"));
                yield return null;
                Check(!ui.recommendation.IsBusy && field.gameObject.activeInHierarchy, "Retry before a first submission stays local and leaves input usable");
                field.text = "";
                Click(Button("Back"));
                yield return WaitFor(() => Button("Order by voice"), "Input returns to ordering methods");
                Check(!speech.Active && !speech.IsRecording && !speech.IsBusy, "Leaving voice input cancels voice state");
                Check(Cursor.visible && player.UiMode, "Back keeps the cursor visible on welcome options");

                Click(Button("Show me the menu"));
                yield return WaitFor(() => VisibleText("Choose your coffee"), "Drink menu");
                Check(!ui.ShouldShowDrinkPreview, "Browsing the menu does not show a default drink preview");
                var offer = CafeRuntimeServices.Menu.GetMenuOffers().FirstOrDefault(o => o.available);
                if (offer != null)
                {
                    var item = CafeRuntimeServices.Menu.GetMenu().items.First(i => i.id == offer.selection.MenuItemId);
                    Click(RawButton(CafeUiLocalization.Text(item.name)));
                    yield return WaitFor(() => Button("Review order"), "Drink configuration");
                    Check(ui.ShouldShowDrinkPreview, "Explicit selection enables the drink preview");
                    Check(VisibleText("Cup size") && VisibleText("Beans") && VisibleText("Milk"), "Configuration labels are localized");
                    Click(Button("Back"));
                    yield return WaitFor(() => VisibleText("Choose your coffee"), "Configuration returns to menu");
                    Check(!ui.ShouldShowDrinkPreview, "Returning to menu hides the preview");
                }
                else Debug.LogWarning("All configurations are sold out: skipped selecting a drink; checking menu Back still continues.");
                Click(Button("Back"));
                yield return WaitFor(() => Button("Order by voice"), "Menu returns to ordering methods instead of cycling to configuration");
                Click(Button("Just browsing"));
                Check(!ui.IsInterfaceVisible && !player.UiMode && Cursor.visible && Cursor.lockState == CursorLockMode.None,
                    "Just browsing closes the panel without hiding or locking the cursor");
                Check(!ui.CanInspectCups, "Closing click cannot pass through to a world object");
                player.SendMessage("OnApplicationFocus", false);
                player.SendMessage("OnApplicationFocus", true);
                Check(!player.UiMode && Cursor.visible, "Focus changes do not create an invisible UI lock during exploration");
                ui.ShowWelcome();
                yield return WaitFor(() => Button("Close"), "Welcome reopened for close check");
                // Exercise preparation input locks without purchasing or starting an animation.
                var preparation = typeof(CafeFrontDeskUI).GetField("purchasing", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(preparation != null, "Preparation state is available for isolated UI simulation");
                try
                {
                    preparation.SetValue(ui, true);
                    player.SetUiMode(false);
                    ui.EndPlayerInteraction();
                    Check(player.UiMode && ui.IsInterfaceVisible && Cursor.visible,
                        "Preparation blocks exit but does not hide the pointer");
                }
                finally { preparation.SetValue(ui, false); }
                Click(Button("Close"));
                yield return null;
                Check(ui.InteractionState == "closed" && !ui.IsInterfaceVisible, "Closing hides the interaction");
                Check(!speech.Active && !speech.IsRecording && !speech.IsBusy, "Closing clears voice activity");
                Check(!player.UiMode && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Closing restores walking with a visible cursor");
                ui.EvaluateReception(player.transform.position, false);
                yield return null;
                ui.EvaluateReception(player.transform.position, false);
                Check(ui.InteractionState == "closed" && !ui.IsInterfaceVisible, "Dismissed reception does not immediately auto-open again");
                // Exit well beyond the outer boundary, then approach the same barista again.
                ui.EvaluateReception(player.transform.position + Vector3.right * 100f, false);
                ui.EvaluateReception(player.transform.position, false);
                Check(ui.IsInterfaceVisible, "Leaving and returning re-arms the automatic greeting");
                ui.EndPlayerInteraction();
                Check(ui.TryOpenInteraction(), "F interaction entry can reopen after dismissal");
                ui.EndPlayerInteraction();

            }
            var explorer = UnityEngine.Object.FindObjectOfType<CafeDesktopPlayer>();
            var bag = GameObject.Find("Coffee Bean Bag");
            if (bag)
            {
                yield return WaitFor(() => ui.CanInspectCups, "World inspection ready after closing");
                Check(ui.InspectBean("bean-blue-mountain", bag.transform), "Bean information can be opened");
                yield return WaitFor(() => Button("Keep exploring"), "Bean information card");
                Check(explorer.UiMode && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Bean information keeps cursor visible and movement paused");
                Click(Button("Keep exploring"));
                Check(!explorer.UiMode && Cursor.visible, "Leaving bean information preserves the cursor");
            }
            ui.ShowWelcome();
            yield return WaitFor(() => Button("Order by voice"), "Welcome for receipt check");
            if (Button("View recent order"))
            {
                Click(Button("View recent order"));
                yield return WaitFor(() => VisibleText("Recent order"), "Recent order receipt");
                Check(explorer.UiMode && Cursor.visible, "Recent receipt keeps the cursor visible");
                Click(Button("Thanks, keep exploring"));
                Check(!explorer.UiMode && Cursor.visible, "Leaving recent receipt preserves the cursor");
            }
            else ui.EndPlayerInteraction();
            Check(CafeRuntimeServices.Inventory.GetInventoryJson() == originalStock, "UI checks never deduct or reset inventory");
            Debug.Log("PASS: " + checks + " conversation UI checks. Covered three languages, timed bubbles, cursor state across browsing, Back, Close, focus changes, preparation locks, bean cards and receipts, plus speech-error isolation and sequential navigation. Live ASR, live LLM, purchasing, animation and visual layout are not tested here.");
        }
        private static IEnumerable<Button> WindowButtons()
        {
            if (!ui || !ui.TerminalCanvas || !ui.TerminalCanvas.isActiveAndEnabled) return Enumerable.Empty<Button>();
            return ui.TerminalCanvas.GetComponentsInChildren<Button>().Where(b => b.isActiveAndEnabled && b.IsInteractable());
        }
        private static Button RawButton(string text) => WindowButtons().FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == text);
        private static Button Button(string english) => RawButton(CafeUiLocalization.Text(english));
        private static bool VisibleText(string english) => ui && ui.TerminalCanvas && ui.TerminalCanvas.isActiveAndEnabled &&
            ui.TerminalCanvas.GetComponentsInChildren<Text>().Any(t => t.isActiveAndEnabled && t.text == CafeUiLocalization.Text(english));
        private static void Click(Button button)
        {
            Check(button && button.isActiveAndEnabled && button.IsInteractable(), "Requested button is available");
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }
        private static IEnumerator WaitFor(Func<bool> condition, string stage, bool advanceManualBubble = true)
        {
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Conversation UI check timed out: " + stage);
                if (advanceManualBubble)
                {
                    var next = ui.GetComponentsInChildren<Button>().FirstOrDefault(b => b.isActiveAndEnabled && b.IsInteractable() && b.name == "Continue");
                    if (next) Click(next);
                }
                yield return null;
            }
            // Allow deferred UI destruction and layout to finish before the next action.
            yield return null;
        }
        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("Conversation UI check failed: " + message);
            checks++;
        }
        private static void Tick()
        {
            if (EditorApplication.isPaused) return;
            if (!Application.isPlaying || !ui) { Cancel(); return; }
            try
            {
                if (!steps.Peek().MoveNext()) steps.Pop();
                else if (steps.Peek().Current is IEnumerator nested) steps.Push(nested);
                if (steps.Count == 0) Cleanup();
            }
            catch (Exception error) { Debug.LogException(error); Cleanup(); }
        }
        private static void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cancel();
        }
        private static void Cancel() { if (steps != null) { Debug.LogWarning("Conversation UI check cancelled."); Cleanup(); } }
        private static void Cleanup()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            steps = null;
            try
            {
                if (ui) ui.EndPlayerInteraction();
                CafeUiLocalization.LanguageIndex = originalLanguage;
                if (ui && ui.recommendation) ui.recommendation.SetConversationLanguage(CafeUiLocalization.LanguageCode);
                if (Application.isPlaying && ui && ui.reception && originalSelection != null)
                {
                    var adapter = ui.reception.menuAdapter;
                    adapter.SelectDrink(originalSelection.MenuItemId);
                    adapter.SelectBean(originalSelection.BeanId); adapter.SelectMilk(originalSelection.MilkId);
                    adapter.SelectCupSize(originalSelection.CupSizeMl); adapter.SetQuantity(originalSelection.Servings);
                }
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { ui = null; speech = null; originalSelection = null; }
        }
    }
}
