using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// Clickable buttons for every town and combat action, so the game can be played with mouse or touch.
    /// Built entirely at runtime by PTManager (no scene setup needed); keyboard shortcuts keep working alongside it.
    /// PTManager calls Refresh() whenever its UI updates, which re-reads costs and what is currently allowed.
    /// </summary>
    public class PTActionBar : MonoBehaviour
    {
        private class ActionButton
        {
            public GameObject root;
            public Button button;
            public TextMeshProUGUI label;
            public Func<bool> isVisible;
            public Func<bool> isEnabled;
            public Func<string> getLabel;
        }

        private static readonly Color PanelColor    = new Color(0.05f, 0.04f, 0.03f, 0.75f);
        private static readonly Color ButtonColor   = new Color(0.30f, 0.22f, 0.12f, 1f);
        private static readonly Color ButtonHover   = new Color(0.42f, 0.31f, 0.16f, 1f);
        private static readonly Color ButtonPressed = new Color(0.20f, 0.15f, 0.08f, 1f);
        private static readonly Color ButtonOff     = new Color(0.18f, 0.16f, 0.14f, 0.6f);
        private static readonly Color TextColor     = new Color(1f, 0.93f, 0.78f, 1f);
        private static readonly Color TextOff       = new Color(1f, 0.93f, 0.78f, 0.35f);

        private const float PanelWidth   = 320f;
        private const float ButtonHeight = 48f;

        private PTManager manager;
        private TextMeshProUGUI header;
        private readonly List<ActionButton> buttons = new List<ActionButton>();

        /// Builds the action bar on its own overlay canvas and returns it.
        public static PTActionBar Create(PTManager manager)
        {
            EnsureEventSystem();

            GameObject root = new GameObject("PT Action Bar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;                                                    // draw above the main HUD

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);                      // same reference as UI_PTonScreen
            scaler.matchWidthOrHeight = 0.5f;

            PTActionBar bar = root.AddComponent<PTActionBar>();
            bar.manager = manager;
            bar.Build(root.GetComponent<RectTransform>());
            bar.Refresh();
            return bar;
        }

        /// Re-reads costs and availability from PTManager and updates every button.
        public void Refresh()
        {
            if (manager == null) return;

            switch (manager.State)
            {
                case PTManager.GameState.Town:     header.text = "Day " + manager.daysElapsed + " - Town"; break;
                case PTManager.GameState.Combat:   header.text = "Battle!"; break;
                case PTManager.GameState.GameOver: header.text = "Game Over"; break;
            }

            foreach (ActionButton action in buttons)
            {
                bool visible = action.isVisible();
                action.root.SetActive(visible);
                if (!visible) continue;

                bool enabled = action.isEnabled();
                action.button.interactable = enabled;
                action.label.text = action.getLabel();
                action.label.color = enabled ? TextColor : TextOff;
            }
        }

        private bool InTown()   => manager.State == PTManager.GameState.Town;
        private bool InCombat() => manager.State == PTManager.GameState.Combat;
        private bool IsOver()   => manager.State == PTManager.GameState.GameOver;

        private void Build(RectTransform canvasRect)
        {
            // Panel docked to the right edge of the screen, growing to fit its visible buttons
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(canvasRect, false);
            panelRect.anchorMin = new Vector2(1f, 0.5f);
            panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-24f, 80f);
            panelRect.sizeDelta = new Vector2(PanelWidth, 0f);
            panel.GetComponent<Image>().color = PanelColor;

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            header = CreateLabel("Header", panelRect);
            header.fontStyle = FontStyles.Bold;
            header.fontSizeMax = 28f;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            // Town
            AddButton(panelRect, InTown, () => !manager.hasFought,
                () => manager.hasFought ? "Already hunted today" : "Hunt goblins  [E]",
                manager.TryEncounter);
            AddButton(panelRect, InTown, () => !manager.PartyFull && manager.gold >= manager.RecruitCost,
                () => manager.PartyFull ? "Party is full" : "Recruit  -" + manager.RecruitCost + "g  [F]",
                manager.TryRecruit);
            AddButton(panelRect, InTown, () => !manager.hasHealed && manager.gold >= manager.HealCost,
                () => manager.hasHealed ? "Cleric is resting" : "Heal party  -" + manager.HealCost + "g  [Q]",
                manager.TryHeal);
            AddButton(panelRect, InTown, () => !manager.hasBeenBlessed && manager.gold >= manager.BlessCost,
                () => manager.hasBeenBlessed ? "Already blessed today" : "Bless party  -" + manager.BlessCost + "g  [1]",
                manager.TryBless);
            AddButton(panelRect, () => InTown() && manager.ResurrectionUnlocked,
                () => manager.FallenCount > 0 && !manager.PartyFull && manager.gold >= manager.CheapestResurrectionCost,
                () => manager.FallenCount == 0 ? "No fallen to raise" : "Resurrect  -" + manager.CheapestResurrectionCost + "g+  [3]",
                manager.TryResurrect);
            AddButton(panelRect, InTown, () => true,
                () => manager.gold >= manager.TotalDailyWages
                    ? "Sleep  (wages " + manager.TotalDailyWages + "g)  [Space]"
                    : "Sleep  (can't pay " + manager.TotalDailyWages + "g!)  [Space]",
                manager.TrySleep);

            // Combat
            AddButton(panelRect, InCombat, () => manager.AwaitingPartyInput,
                () => manager.AwaitingPartyInput ? manager.ActiveActorName + " attacks  [Click]" : "Enemies are acting...",
                manager.TryAttack);
            AddButton(panelRect, InCombat, () => manager.AwaitingPartyInput,
                () => "Run away  -" + manager.FleeCost + "g  [R]",
                manager.TryRunAway);

            // Game over
            AddButton(panelRect, IsOver, () => true, () => "Start a new run  [Esc]", manager.RestartRun);
        }

        private void AddButton(RectTransform parent, Func<bool> isVisible, Func<bool> isEnabled, Func<string> getLabel, Action onClick)
        {
            GameObject root = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.GetComponent<RectTransform>().SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = ButtonHeight;

            Image image = root.GetComponent<Image>();
            image.color = Color.white;                                                   // tinted by the button's colour block below

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHover;
            colors.selectedColor = ButtonColor;
            colors.pressedColor = ButtonPressed;
            colors.disabledColor = ButtonOff;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };          // arrow keys shouldn't move focus between actions
            button.onClick.AddListener(() =>
            {
                onClick();
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);                     // don't leave the button selected, or Submit (Enter) would press it again
                }
            });

            TextMeshProUGUI label = CreateLabel("Label", root.GetComponent<RectTransform>());
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 4f);
            labelRect.offsetMax = new Vector2(-10f, -4f);

            buttons.Add(new ActionButton
            {
                root = root,
                button = button,
                label = label,
                isVisible = isVisible,
                isEnabled = isEnabled,
                getLabel = getLabel,
            });
        }

        private static TextMeshProUGUI CreateLabel(string name, RectTransform parent)
        {
            GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.GetComponent<RectTransform>().SetParent(parent, false);
            TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();                // uses the default font from TMP Settings
            text.alignment = TextAlignmentOptions.Center;
            text.color = TextColor;
            text.raycastTarget = false;
            text.enableAutoSizing = true;                                                  // long hero names shrink to fit
            text.fontSizeMin = 14f;
            text.fontSizeMax = 22f;
            return text;
        }

        private static void EnsureEventSystem()                                            // buttons need an EventSystem to receive clicks
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
