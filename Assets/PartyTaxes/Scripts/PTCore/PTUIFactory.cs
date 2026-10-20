using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// Small helpers for building uGUI at runtime (canvases, panels, rows, labels, buttons) in one consistent style.
    /// Used by PTActionBar and PTFinalLedgerPanel so neither needs scene or prefab setup.
    /// </summary>
    public static class PTUIFactory
    {
        public static readonly Color PanelColor    = new Color(0.05f, 0.04f, 0.03f, 0.85f);
        public static readonly Color ButtonColor   = new Color(0.30f, 0.22f, 0.12f, 1f);
        public static readonly Color ButtonHover   = new Color(0.42f, 0.31f, 0.16f, 1f);
        public static readonly Color ButtonPressed = new Color(0.20f, 0.15f, 0.08f, 1f);
        public static readonly Color ButtonOff     = new Color(0.18f, 0.16f, 0.14f, 0.6f);
        public static readonly Color TextColor     = new Color(1f, 0.93f, 0.78f, 1f);
        public static readonly Color TextOff       = new Color(1f, 0.93f, 0.78f, 0.35f);

        /// An overlay canvas scaled like UI_PTonScreen (1920x1080 reference).
        public static RectTransform CreateCanvas(string name, int sortingOrder)
        {
            EnsureEventSystem();
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return root.GetComponent<RectTransform>();
        }

        /// A vertical panel that grows to fit its children.
        public static RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchor, Vector2 position, float width, int padding = 12, float spacing = 6f)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 0f);
            panel.GetComponent<Image>().color = PanelColor;

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        /// A horizontal row inside a vertical panel; children share its width.
        public static RectTransform CreateRow(RectTransform parent, float height, float spacing = 6f)
        {
            GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = height;
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rect;
        }

        public static TextMeshProUGUI CreateLabel(string name, RectTransform parent, float fontSizeMax = 22f, TextAlignmentOptions alignment = TextAlignmentOptions.Center, float preferredHeight = -1f)
        {
            GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.GetComponent<RectTransform>().SetParent(parent, false);
            TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();                // uses the default font from TMP Settings
            text.alignment = alignment;
            text.color = TextColor;
            text.raycastTarget = false;
            text.enableAutoSizing = true;                                                  // long names shrink to fit
            text.fontSizeMin = 12f;
            text.fontSizeMax = fontSizeMax;
            if (preferredHeight > 0f)
            {
                textObj.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            }
            return text;
        }

        public static Button CreateButton(RectTransform parent, float height, Action onClick, out TextMeshProUGUI label)
        {
            GameObject root = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.GetComponent<RectTransform>().SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = height;

            Image image = root.GetComponent<Image>();
            image.color = Color.white;                                                     // tinted by the colour block below

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHover;
            colors.selectedColor = ButtonColor;
            colors.pressedColor = ButtonPressed;
            colors.disabledColor = ButtonOff;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };            // arrow keys shouldn't move focus between actions
            button.onClick.AddListener(() =>
            {
                onClick();
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);                       // don't leave it selected, or Submit (Enter) would press it again
                }
            });

            label = CreateLabel("Label", root.GetComponent<RectTransform>(), 20f);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 3f);
            labelRect.offsetMax = new Vector2(-8f, -3f);
            return button;
        }

        public static void EnsureEventSystem()                                             // buttons need an EventSystem to receive clicks
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
