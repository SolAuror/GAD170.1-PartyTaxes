using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    public enum PTCardStyle { Notice, Letter, Report }
    public enum PTPlateStyle { Slip, Note, Banner, Tag }

    /// <summary>
    /// Small helpers for building uGUI at runtime in the paper-and-ink style from PTUITheme, from basic pieces (canvases,
    /// panels, rows, labels, buttons) to the diegetic objects the UI is made of (ledger entries, tacked notices, sealed
    /// letters, signpost planks, scroll rods). Used by PTActionBar, PTRunSetupPanel, PTFinalLedgerPanel and PTWorldTag,
    /// and by PTManager / PTAdventureLog to restyle the HUD prefab when the game starts.
    /// </summary>
    public static class PTUIFactory
    {
        public static readonly Color TextColor = PTUITheme.Ink;
        public static readonly Color TextOff   = PTUITheme.InkOff;

        private const float PanelBorderScale  = 2f;                                        // PaperPanel's 128px border shows as 64 units
        private const float ButtonBorderScale = 2.5f;                                      // slips' 40px border shows as 16 units
        private const float PlateBorderScale  = 2.5f;

        /// An overlay canvas scaled like UI_PTonScreen (1920x1080 reference). A backdrop dims the game behind it.
        public static RectTransform CreateCanvas(string name, int sortingOrder, bool backdrop = false)
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
            RectTransform rect = root.GetComponent<RectTransform>();
            if (backdrop)
            {
                Image dim = CreateImage("Backdrop", rect, PTUITheme.Vignette, PTUITheme.Backdrop);
                Stretch(dim.rectTransform);
                dim.raycastTarget = true;                                                  // also blocks clicks to the HUD underneath
            }
            return rect;
        }

        /// A vertical sheet of parchment that grows to fit its children.
        public static RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchor, Vector2 position, float width, int padding = 30, float spacing = 6f)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 0f);
            ApplyPaper(panel.GetComponent<Image>(), PTUITheme.PaperPanel, PanelBorderScale, PTUITheme.PaperFlat);

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

        /// Body text in ink (Crimson Text).
        public static TextMeshProUGUI CreateLabel(string name, RectTransform parent, float fontSizeMax = 22f, TextAlignmentOptions alignment = TextAlignmentOptions.Center, float preferredHeight = -1f)
        {
            GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.GetComponent<RectTransform>().SetParent(parent, false);
            TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();
            PTUITheme.SetFont(text, PTUITheme.Body);
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

        /// A printed small-caps header (IM Fell English SC).
        public static TextMeshProUGUI CreateHeading(string name, RectTransform parent, float fontSizeMax, TextAlignmentOptions alignment, float preferredHeight)
        {
            TextMeshProUGUI text = CreateLabel(name, parent, fontSizeMax, alignment, preferredHeight);
            PTUITheme.SetFont(text, PTUITheme.Heading);
            return text;
        }

        /// A blackletter royal title (Pirata One); falls back to a bold heading.
        public static TextMeshProUGUI CreateTitle(string name, RectTransform parent, float fontSizeMax, float preferredHeight)
        {
            TextMeshProUGUI text = CreateLabel(name, parent, fontSizeMax, TextAlignmentOptions.Center, preferredHeight);
            if (PTUITheme.Title != null) PTUITheme.SetFont(text, PTUITheme.Title);
            else text.fontStyle = FontStyles.Bold;
            return text;
        }

        /// An ink flourish to separate sections.
        public static void CreateDivider(RectTransform parent, float height = 22f)
        {
            Image line = CreateImage("Divider", parent, PTUITheme.InkDivider, PTUITheme.InkDivider != null ? Color.white : PTUITheme.InkOff);
            line.preserveAspect = PTUITheme.InkDivider != null;
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = PTUITheme.InkDivider != null ? height : 2f;
        }

        public static Button CreateButton(RectTransform parent, float height, Action onClick, out TextMeshProUGUI label, PTButtonStyle style = PTButtonStyle.Paper)
        {
            GameObject root = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.GetComponent<RectTransform>().SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = height;

            Image image = root.GetComponent<Image>();
            bool wax = style == PTButtonStyle.Wax;
            ApplyPaper(image, wax ? PTUITheme.WaxSlip : PTUITheme.PaperSlip, ButtonBorderScale, wax ? PTUITheme.WaxFlat : PTUITheme.PaperFlat);

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = ColorBlock.defaultColorBlock;                              // tints multiply the paper, so keep them warm and light
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.84f, 1f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.80f, 0.72f, 0.60f, 1f);
            colors.disabledColor = new Color(0.92f, 0.90f, 0.86f, 0.55f);
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
            label.color = PTUITheme.ButtonText(style, true);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(14f, 4f);                                    // inside the slip's ink rule
            labelRect.offsetMax = new Vector2(-14f, -4f);
            return button;
        }

        /// A rubber stamp (e.g. PARDONED) laid over a document at an angle; ignores the layout it sits in.
        public static void CreateStamp(RectTransform parent, string word, Color ink, Vector2 anchor, Vector2 position, Vector2 size, float angle)
        {
            Image frame = CreateImage("Stamp", parent, PTUITheme.Stamp, ink);
            if (PTUITheme.Stamp != null)
            {
                frame.type = Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier = 2f;
            }
            else
            {
                frame.color = Color.clear;                                                 // no art: just the stamped word
            }
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Place(frame.rectTransform, anchor, position, size);
            frame.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

            TextMeshProUGUI text = CreateHeading("Word", frame.rectTransform, size.y * 0.5f, TextAlignmentOptions.Center, -1f);
            text.text = word.ToUpperInvariant();
            text.color = ink;
            text.characterSpacing = 8f;
            Stretch(text.rectTransform, 20f);
        }

        /// A decorative image outside the layout (wax seal on a corner, etc.).
        public static Image CreateOrnament(string name, RectTransform parent, Sprite sprite, Vector2 anchor, Vector2 position, Vector2 size, float angle)
        {
            if (sprite == null) return null;
            Image image = CreateImage(name, parent, sprite, Color.white);
            image.preserveAspect = true;
            image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Place(image.rectTransform, anchor, position, size);
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return image;
        }

        /// Thin red ledger rules down a table, just left of each column (fractions of the table's width).
        public static void AddColumnRules(RectTransform table, float[] columns)
        {
            foreach (float column in columns)
            {
                Image rule = CreateImage("Rule", table, null, PTUITheme.LedgerRule);
                rule.rectTransform.anchorMin = new Vector2(column, 0f);
                rule.rectTransform.anchorMax = new Vector2(column, 1f);
                rule.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rule.rectTransform.sizeDelta = new Vector2(2f, 0f);
                rule.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            }
        }

        // ── Diegetic objects: the ledger book, notices, letters and the signpost ──

        /// A soft drop shadow so a sheet reads as an object lying over the scene.
        public static void AddShadow(Graphic graphic, Vector2 distance)
        {
            Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = distance;
        }

        /// A ledger's red double margin line, x units from the page's left edge. Returns both lines (to hide them).
        public static GameObject[] AddMarginLines(RectTransform page, float x)
        {
            GameObject[] lines = new GameObject[2];
            for (int i = 0; i < 2; i++)
            {
                Image line = CreateImage("Margin", page, null, PTUITheme.MarginRed);
                line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Anchor(line.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(x + i * 4f, 30f), new Vector2(x + i * 4f + 1.5f, -24f));
                lines[i] = line.gameObject;
            }
            return lines;
        }

        /// A line in the ledger: hotkey in the margin, the entry, and its cost in the debit column.
        /// Hovering washes the line in ochre ink, like running a highlighter down the books.
        public static Button CreateEntry(RectTransform parent, float height, Action onClick, out TextMeshProUGUI key, out TextMeshProUGUI label, out TextMeshProUGUI amount)
        {
            GameObject root = new GameObject("Entry", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = height;

            Image wash = root.GetComponent<Image>();
            ApplyPaper(wash, PTUITheme.InkWash, 2f, Color.white);
            wash.color = PTUITheme.Highlight;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = wash;
            ColorBlock colors = ColorBlock.defaultColorBlock;                              // invisible until hovered
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.5f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.85f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                onClick();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });

            Anchor(CreateImage("Ruling", rect, null, PTUITheme.LedgerLine).rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1.5f));
            Anchor(CreateImage("Debit", rect, null, PTUITheme.LedgerRule).rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-88f, -3f), new Vector2(-86.5f, 3f));

            key = CreateHeading("Key", rect, 17f, TextAlignmentOptions.Center, -1f);
            key.color = PTUITheme.InkRed;
            Anchor(key.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 2f), new Vector2(34f, 0f));
            label = CreateLabel("Label", rect, 19f, TextAlignmentOptions.Left);
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(46f, 2f), new Vector2(-96f, 0f));
            amount = CreateLabel("Amount", rect, 19f, TextAlignmentOptions.Right);
            Anchor(amount.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-84f, 2f), new Vector2(-4f, 0f));
            return button;
        }

        /// A sheet laid on a surface: a notice tacked to the board, a letter with a wax seal, or an incident report
        /// with a stamp across it. Children stack inside it; the tilt makes each one look placed by hand.
        public static RectTransform CreateCard(RectTransform parent, PTCardStyle style, float tilt, string stampWord = null)
        {
            GameObject card = new GameObject(style + " Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image paper = card.GetComponent<Image>();
            if (style == PTCardStyle.Notice) ApplyPaper(paper, PTUITheme.PaperPanel, 4f, PTUITheme.PaperFlat);
            else ApplyPaper(paper, PTUITheme.PaperSlip, ButtonBorderScale, PTUITheme.PaperFlat);
            AddShadow(paper, new Vector2(3f, -4f));

            VerticalLayoutGroup layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, style == PTCardStyle.Notice ? 26 : 18, 16);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);

            switch (style)
            {
                case PTCardStyle.Notice:
                    CreateOrnament("Pin", rect, PTUITheme.Pin, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(26f, 26f), 0f);
                    break;
                case PTCardStyle.Letter:
                    CreateOrnament("Seal", rect, PTUITheme.WaxSeal, new Vector2(1f, 1f), new Vector2(-22f, -18f), new Vector2(50f, 50f), 14f);
                    break;
                case PTCardStyle.Report:
                    CreateStamp(rect, stampWord ?? "Caught", PTUITheme.StampRed, new Vector2(1f, 1f), new Vector2(-74f, -26f), new Vector2(128f, 48f), 8f);
                    break;
            }
            return rect;
        }

        /// One board on the town signpost: an arrow-shaped plank with the place painted on it.
        public static Button CreatePlank(RectTransform parent, Action onClick, out TextMeshProUGUI label, out LayoutElement size)
        {
            GameObject root = new GameObject("Plank", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.GetComponent<RectTransform>().SetParent(parent, false);
            size = root.GetComponent<LayoutElement>();
            size.preferredHeight = 46f;
            size.preferredWidth = 210f;
            Image wood = root.GetComponent<Image>();
            ApplyPaper(wood, PTUITheme.Plank, 2f, new Color(0.42f, 0.28f, 0.16f, 1f));
            AddShadow(wood, new Vector2(2f, -3f));

            Button button = root.GetComponent<Button>();
            button.targetGraphic = wood;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);                     // a touch dark, so hover can brighten it
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.pressedColor = new Color(0.66f, 0.62f, 0.58f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                onClick();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });

            label = CreateHeading("Place", root.GetComponent<RectTransform>(), 22f, TextAlignmentOptions.Center, -1f);
            label.color = PTUITheme.Painted;
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(34f, 3f), new Vector2(-26f, -3f));
            return button;
        }

        /// A wooden signpost beside a panel (outside its layout), holding planks top to bottom.
        public static RectTransform CreateSignpost(RectTransform panel)
        {
            GameObject post = new GameObject("Signpost", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            RectTransform rect = post.GetComponent<RectTransform>();
            rect.SetParent(panel, false);
            post.GetComponent<LayoutElement>().ignoreLayout = true;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-2f, -54f);
            VerticalLayoutGroup layout = post.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = post.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Image pole = CreateImage("Pole", rect, null, new Color(0.30f, 0.20f, 0.12f, 1f));
            pole.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Anchor(pole.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-26f, -70f), new Vector2(-12f, 18f));
            return rect;
        }

        /// The wooden rods of a scroll across the top and bottom of a document of the given width.
        public static void AddScrollRods(RectTransform panel, float width)
        {
            if (PTUITheme.ScrollRod == null) return;
            for (int end = 0; end < 2; end++)
            {
                Image rod = CreateImage("Rod", panel, null, Color.white);
                ApplyPaper(rod, PTUITheme.ScrollRod, 1.4f, Color.clear);
                rod.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Place(rod.rectTransform, new Vector2(0.5f, end), new Vector2(0f, end == 0 ? 6f : -6f), new Vector2(46f, width + 60f));
                rod.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);           // the art is vertical
            }
        }

        // ── Restyling the HUD prefab ──────────────────────────────────

        /// Puts an existing HUD text on an object that hugs it, pinned to a screen edge or corner (anchor doubles as the
        /// pivot, so a note in the top-left grows down and right as its text changes): a note tacked up, a cloth banner,
        /// a hang tag on twine, or a plain paper slip. Returns the plate (its Image can be re-tinted, e.g. the banner).
        public static RectTransform WrapInPlate(TextMeshProUGUI text, Vector2 anchor, Vector2 position, TMP_FontAsset font, float fontSize,
                                                PTPlateStyle style = PTPlateStyle.Slip, Sprite icon = null)
        {
            if (text == null) return null;
            RectTransform textRect = text.rectTransform;
            GameObject plate = new GameObject(text.name + " Plate", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform rect = plate.GetComponent<RectTransform>();
            rect.SetParent(textRect.parent, false);
            rect.SetSiblingIndex(textRect.GetSiblingIndex());
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            Image image = plate.GetComponent<Image>();

            HorizontalLayoutGroup layout = plate.GetComponent<HorizontalLayoutGroup>();
            Color textColor = TextColor;
            switch (style)
            {
                case PTPlateStyle.Note:
                    ApplyPaper(image, PTUITheme.PaperPanel, 4f, PTUITheme.PaperFlat);
                    layout.padding = new RectOffset(28, 28, 28, 20);
                    rect.localRotation = Quaternion.Euler(0f, 0f, -2f);
                    CreateOrnament("Pin", rect, PTUITheme.Pin, new Vector2(0.5f, 1f), new Vector2(0f, -13f), new Vector2(28f, 28f), 0f);
                    break;
                case PTPlateStyle.Banner:
                    ApplyPaper(image, PTUITheme.Banner, 2f, PTUITheme.BannerTown);
                    if (PTUITheme.Banner != null) image.color = PTUITheme.BannerTown;
                    layout.padding = new RectOffset(124, 124, 12, 32);                     // the cloth band, between the tails
                    textColor = PTUITheme.Painted;
                    break;
                case PTPlateStyle.Tag:
                    ApplyPaper(image, PTUITheme.Tag, 2.5f, PTUITheme.PaperFlat);
                    layout.padding = new RectOffset(26, 26, 32, 14);
                    Image twine = CreateImage("Twine", rect, null, PTUITheme.Twine);
                    twine.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    Anchor(twine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-1.5f, -16f), new Vector2(1.5f, 26f));
                    CreateOrnament("Eyelet", rect, PTUITheme.Eyelet, new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(20f, 20f), 0f);
                    break;
                default:
                    ApplyPaper(image, PTUITheme.PaperSlip, PlateBorderScale, PTUITheme.PaperFlat);
                    layout.padding = new RectOffset(24, 24, 12, 12);
                    break;
            }
            if (style != PTPlateStyle.Banner) AddShadow(image, new Vector2(3f, -4f));

            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = plate.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (icon != null)
            {
                Image iconImage = CreateImage("Icon", rect, icon, Color.white);
                iconImage.preserveAspect = true;
                LayoutElement iconSize = iconImage.gameObject.AddComponent<LayoutElement>();
                iconSize.preferredWidth = fontSize * 1.15f;
                iconSize.preferredHeight = fontSize * 1.15f;
            }

            textRect.SetParent(rect, false);
            PTUITheme.SetFont(text, font);
            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.color = textColor;
            text.alignment = style == PTPlateStyle.Banner ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            return rect;
        }

        /// The adventure log: ink on a strip of parchment, with an inked scrollbar.
        public static void StyleLog(ScrollRect scroll, TextMeshProUGUI entryTemplate)
        {
            PTUITheme.SetFont(entryTemplate, PTUITheme.Body);
            if (scroll == null) return;
            ApplyPaper(scroll.GetComponent<Image>(), PTUITheme.PaperPanel, 5f, PTUITheme.PaperFlat);
            if (scroll.viewport != null)
            {
                scroll.viewport.offsetMin = new Vector2(20f, 8f);                          // keep entries off the paper's torn edge
                scroll.viewport.offsetMax = new Vector2(-32f, -8f);
            }
            if (PTUITheme.ScrollRod != null)                                               // rolled ends: the log is a scroll
            {
                RectTransform log = scroll.GetComponent<RectTransform>();
                for (int end = 0; end < 2; end++)
                {
                    Image rod = CreateImage("ScrollEnd", log, null, Color.white);
                    ApplyPaper(rod, PTUITheme.ScrollRod, 2f, Color.clear);
                    Anchor(rod.rectTransform, new Vector2(end, 0f), new Vector2(end, 1f),
                           new Vector2(end == 0 ? -22f : -10f, -18f), new Vector2(end == 0 ? 10f : 22f, 18f));
                }
            }
            Transform backing = scroll.transform.parent;                                   // the old tinted band behind the log
            Image band = backing != null ? backing.GetComponent<Image>() : null;
            if (band != null) band.color = Color.clear;
            Scrollbar bar = scroll.verticalScrollbar;
            if (bar != null)
            {
                RectTransform barRect = bar.GetComponent<RectTransform>();
                if (barRect != null)
                {
                    barRect.anchoredPosition = new Vector2(-14f, 0f);
                    barRect.sizeDelta = new Vector2(10f, -24f);
                }
                Image track = bar.GetComponent<Image>();
                if (track != null) track.color = new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.12f);
                if (bar.handleRect != null && bar.handleRect.GetComponent<Image>() != null)
                {
                    bar.handleRect.GetComponent<Image>().color = new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.6f);
                }
            }
        }

        // ── Plumbing ──────────────────────────────────────────────────
        public static void EnsureEventSystem()                                             // buttons need an EventSystem to receive clicks
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// Sliced paper art on an Image, or a flat colour if the art didn't load.
        private static void ApplyPaper(Image image, Sprite sprite, float borderScale, Color flat)
        {
            if (image == null) return;
            if (sprite == null)
            {
                image.sprite = null;
                image.color = flat;
                return;
            }
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = borderScale;
            image.color = Color.white;
        }

        public static Image CreateImage(string name, RectTransform parent, Sprite sprite, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = obj.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// Sets a rect's anchors and its offsets from them in one go (offsets as in the inspector's Left/Bottom and Right/Top).
        public static RectTransform Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        /// Sliced art on an Image (borderScale divides the sprite's border), or a flat colour if the art didn't load.
        public static void Slice(Image image, Sprite sprite, float borderScale, Color flat)
        {
            ApplyPaper(image, sprite, borderScale, flat);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
