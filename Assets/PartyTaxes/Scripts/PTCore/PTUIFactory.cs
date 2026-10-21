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
    /// Helpers for building uGUI in the paper-and-ink style from PTUITheme, from basic pieces (canvases, panels, rows,
    /// labels, buttons) to the diegetic objects the UI is made of (ledger entries, tacked notices, sealed letters,
    /// signpost planks, scroll rods). Used by PTActionBar, PTRunSetupPanel, PTFinalLedgerPanel and PTWorldTag (through
    /// PTUIBuilder, so the same code can bake them), and to restyle the HUD prefab.
    ///
    /// Sizing rules: decorative art sits on a "Background" child that layout ignores (a sliced Image would otherwise
    /// report its whole border as a minimum size and inflate the layout), and text sizes itself to its content.
    /// The helpers that add decoration to an existing object are safe to call again: they reuse what's there.
    /// </summary>
    public static class PTUIFactory
    {
        public static readonly Color TextColor = PTUITheme.Ink;
        public static readonly Color TextOff   = PTUITheme.InkOff;

        public const string Background = "Background";
        private const float PanelBorderScale  = 2f;                                        // PaperPanel's 128px border shows as 64 units
        private const float ButtonBorderScale = 2.5f;                                      // slips' 40px border shows as 16 units
        private const float PlateBorderScale  = 2.5f;

        // ── Containers ────────────────────────────────────────────────

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

        /// A vertical sheet (parchment unless another surface is given) that grows to fit its children.
        public static RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchor, Vector2 position, float width, RectOffset padding,
                                                float spacing = 6f, Sprite surface = null)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 0f);
            SetBackground(rect, surface != null ? surface : PTUITheme.PaperPanel, PanelBorderScale, PTUITheme.PaperFlat, true);

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = padding;
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

        /// An invisible vertical group inside a panel (one per view, so a view can be shown, hidden or edited as one).
        public static RectTransform CreateGroup(string name, RectTransform parent, float spacing = 6f)
        {
            GameObject group = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            RectTransform rect = group.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            VerticalLayoutGroup layout = group.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        /// A horizontal row of a fixed height; children share its width.
        public static RectTransform CreateRow(RectTransform parent, float height, float spacing = 6f)
        {
            GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            LayoutElement size = row.GetComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;
            size.flexibleHeight = 0f;                                                      // never soaks up a parent's spare height
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rect;
        }

        // ── Text ──────────────────────────────────────────────────────

        /// Body text in ink (Crimson Text). By default it keeps its size and wraps, and its height follows its text.
        /// fit = shrink to fit a fixed box instead (single-line labels on buttons and the like).
        /// height > 0 fixes its height in a layout.
        public static TextMeshProUGUI CreateLabel(string name, RectTransform parent, float size = PTUITheme.SizeBody,
                                                  TextAlignmentOptions alignment = TextAlignmentOptions.Center, bool fit = false, float height = -1f)
        {
            GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.GetComponent<RectTransform>().SetParent(parent, false);
            TextMeshProUGUI text = textObj.GetComponent<TextMeshProUGUI>();
            PTUITheme.SetFont(text, PTUITheme.Body);
            text.alignment = alignment;
            text.color = TextColor;
            text.raycastTarget = false;
            text.fontSize = size;
            text.enableAutoSizing = fit;
            text.fontSizeMax = size;
            text.fontSizeMin = Mathf.Max(13f, Mathf.Round(size * 0.72f));                  // never shrinks past readable
            if (height > 0f)
            {
                LayoutElement fixedHeight = textObj.AddComponent<LayoutElement>();
                fixedHeight.minHeight = height;
                fixedHeight.preferredHeight = height;
            }
            return text;
        }

        /// A printed small-caps header (IM Fell English SC), fitted to one line.
        public static TextMeshProUGUI CreateHeading(string name, RectTransform parent, float size, TextAlignmentOptions alignment, float height = -1f, bool fit = true)
        {
            TextMeshProUGUI text = CreateLabel(name, parent, size, alignment, fit, height > 0f ? height : (fit ? size * 1.3f : -1f));
            PTUITheme.SetFont(text, PTUITheme.Heading);
            return text;
        }

        /// A blackletter royal title (Pirata One); falls back to a bold heading.
        public static TextMeshProUGUI CreateTitle(string name, RectTransform parent, float size, float height)
        {
            TextMeshProUGUI text = CreateLabel(name, parent, size, TextAlignmentOptions.Center, true, height);
            if (PTUITheme.Title != null) PTUITheme.SetFont(text, PTUITheme.Title);
            else text.fontStyle = FontStyles.Bold;
            return text;
        }

        /// An ink flourish to separate sections.
        public static Image CreateDivider(RectTransform parent, float height = 22f)
        {
            Image line = CreateImage("Divider", parent, PTUITheme.InkDivider, PTUITheme.InkDivider != null ? Color.white : PTUITheme.InkOff);
            line.preserveAspect = PTUITheme.InkDivider != null;
            LayoutElement size = line.gameObject.AddComponent<LayoutElement>();
            size.minHeight = PTUITheme.InkDivider != null ? height : 2f;
            size.preferredHeight = size.minHeight;
            return line;
        }

        // ── Buttons ───────────────────────────────────────────────────

        /// A paper slip (or wax, for the main action) with a one-line label that shrinks to fit. Add the click with OnClick.
        public static Button CreateButton(RectTransform parent, float height, PTButtonStyle style = PTButtonStyle.Paper, float textSize = PTUITheme.SizeButton)
        {
            GameObject root = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.GetComponent<RectTransform>().SetParent(parent, false);
            LayoutElement size = root.GetComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;

            Image image = root.GetComponent<Image>();
            bool wax = style == PTButtonStyle.Wax;
            Slice(image, wax ? PTUITheme.WaxSlip : PTUITheme.PaperSlip, ButtonBorderScale, wax ? PTUITheme.WaxFlat : PTUITheme.PaperFlat);

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

            TextMeshProUGUI label = CreateLabel("Label", root.GetComponent<RectTransform>(), textSize, TextAlignmentOptions.Center, true);
            label.color = PTUITheme.ButtonText(style, true);
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 3f), new Vector2(-14f, -3f));   // inside the slip's ink rule
            return button;
        }

        /// The label of a button, entry or plank (the child called name).
        public static TextMeshProUGUI LabelOf(Component element, string name = "Label")
        {
            Transform child = element.transform.Find(name);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : element.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        /// Wires a click (done at runtime, so it also works on baked buttons).
        public static void OnClick(Button button, Action onClick)
        {
            button.onClick.AddListener(() =>
            {
                onClick();
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);                       // don't leave it selected, or Submit (Enter) would press it again
                }
            });
        }

        /// A line in the ledger: "Key" (hotkey in the margin), "Label" (the entry) and "Amount" (cost, in the debit column).
        /// Hovering washes the line in ochre ink, like running a highlighter down the books.
        public static Button CreateEntry(RectTransform parent, float height)
        {
            GameObject root = new GameObject("Entry", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            LayoutElement size = root.GetComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;

            Image wash = root.GetComponent<Image>();
            Slice(wash, PTUITheme.InkWash, 2f, Color.white);
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

            Anchor(CreateImage("Ruling", rect, null, PTUITheme.LedgerLine).rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1.5f));
            Anchor(CreateImage("Debit", rect, null, PTUITheme.LedgerRule).rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-88f, -3f), new Vector2(-86.5f, 3f));

            TextMeshProUGUI key = CreateLabel("Key", rect, 19f, TextAlignmentOptions.Center, true);
            PTUITheme.SetFont(key, PTUITheme.Heading);
            key.color = PTUITheme.InkRed;
            Anchor(key.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 2f), new Vector2(34f, 0f));
            TextMeshProUGUI label = CreateLabel("Label", rect, PTUITheme.SizeBody, TextAlignmentOptions.Left, true);
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(46f, 2f), new Vector2(-96f, 0f));
            TextMeshProUGUI amount = CreateLabel("Amount", rect, PTUITheme.SizeBody, TextAlignmentOptions.Right, true);
            Anchor(amount.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-84f, 2f), new Vector2(-4f, 0f));
            return button;
        }

        /// One board on the town signpost: an arrow-shaped plank with the place painted on it.
        public static Button CreatePlank(RectTransform parent, float width, float height)
        {
            GameObject root = new GameObject("Plank", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.GetComponent<RectTransform>().SetParent(parent, false);
            LayoutElement size = root.GetComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;
            size.preferredWidth = width;
            Image wood = root.GetComponent<Image>();
            Slice(wood, PTUITheme.Plank, 2.2f, new Color(0.42f, 0.28f, 0.16f, 1f));
            AddShadow(wood, new Vector2(2f, -3f));

            Button button = root.GetComponent<Button>();
            button.targetGraphic = wood;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);                     // a touch dark, so hover can brighten it
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.pressedColor = new Color(0.66f, 0.62f, 0.58f, 1f);
            colors.disabledColor = Color.white;                                            // the board you're at isn't greyed out
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            TextMeshProUGUI label = CreateLabel("Label", root.GetComponent<RectTransform>(), 20f, TextAlignmentOptions.Center, true);
            PTUITheme.SetFont(label, PTUITheme.Heading);
            label.color = PTUITheme.Painted;
            Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 3f), new Vector2(-22f, -3f));
            return button;
        }

        /// A wooden signpost beside a panel (outside its layout), holding planks top to bottom.
        public static RectTransform CreateSignpost(RectTransform panel, Vector2 position)
        {
            GameObject post = new GameObject("Signpost", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            RectTransform rect = post.GetComponent<RectTransform>();
            rect.SetParent(panel, false);
            post.GetComponent<LayoutElement>().ignoreLayout = true;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            VerticalLayoutGroup layout = post.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
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
            Anchor(pole.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-24f, -40f), new Vector2(-11f, 14f));
            return rect;
        }

        // ── Documents ─────────────────────────────────────────────────

        /// A sheet laid on a surface: a notice tacked to the board, a letter with a wax seal, or an incident report
        /// with a stamp across it. Children stack inside it; the tilt makes each one look placed by hand.
        public static RectTransform CreateCard(RectTransform parent, PTCardStyle style, float tilt, string stampWord = null)
        {
            GameObject card = new GameObject(style + " Card", typeof(RectTransform), typeof(VerticalLayoutGroup));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image paper = style == PTCardStyle.Notice
                ? SetBackground(rect, PTUITheme.PaperPanel, 4f, PTUITheme.PaperFlat, true)
                : SetBackground(rect, PTUITheme.PaperSlip, ButtonBorderScale, PTUITheme.PaperFlat, true);
            AddShadow(paper, new Vector2(3f, -4f));

            VerticalLayoutGroup layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, style == PTCardStyle.Notice ? 24 : 16, 16);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);

            switch (style)
            {
                case PTCardStyle.Notice:
                    CreateOrnament("Pin", rect, PTUITheme.Pin, new Vector2(0.5f, 1f), new Vector2(0f, -11f), new Vector2(24f, 24f), 0f);
                    break;
                case PTCardStyle.Letter:
                    CreateOrnament("Seal", rect, PTUITheme.WaxSeal, new Vector2(1f, 1f), new Vector2(-20f, -16f), new Vector2(46f, 46f), 14f);
                    break;
                case PTCardStyle.Report:
                    CreateStamp(rect, stampWord ?? "Caught", PTUITheme.StampRed, new Vector2(1f, 1f), new Vector2(-74f, -26f), new Vector2(128f, 48f), 8f);
                    break;
            }
            return rect;
        }

        /// A rubber stamp (e.g. PARDONED) laid over a document at an angle; ignores the layout it sits in.
        public static RectTransform CreateStamp(RectTransform parent, string word, Color ink, Vector2 anchor, Vector2 position, Vector2 size, float angle)
        {
            Image frame = CreateImage("Stamp", parent, null, ink);
            Slice(frame, PTUITheme.Stamp, 2f, Color.clear);                                 // no art: just the stamped word
            if (PTUITheme.Stamp != null) frame.color = ink;
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Place(frame.rectTransform, anchor, position, size);
            frame.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

            TextMeshProUGUI text = CreateLabel("Word", frame.rectTransform, size.y * 0.5f, TextAlignmentOptions.Center, true);
            PTUITheme.SetFont(text, PTUITheme.Heading);
            text.characterSpacing = 8f;
            Stretch(text.rectTransform, size.y * 0.18f);
            SetStamp(frame.rectTransform, word, ink);
            return frame.rectTransform;
        }

        /// Re-inks a stamp with a new word and colour.
        public static void SetStamp(RectTransform stamp, string word, Color ink)
        {
            Image frame = stamp.GetComponent<Image>();
            if (frame != null && frame.sprite != null) frame.color = ink;
            TextMeshProUGUI text = LabelOf(stamp, "Word");
            if (text == null) return;
            text.text = word.ToUpperInvariant();
            text.color = ink;
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
            for (int i = 0; i < columns.Length; i++)
            {
                if (table.Find("Rule " + i) != null) continue;
                Image rule = CreateImage("Rule " + i, table, null, PTUITheme.LedgerRule);
                rule.rectTransform.anchorMin = new Vector2(columns[i], 0f);
                rule.rectTransform.anchorMax = new Vector2(columns[i], 1f);
                rule.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rule.rectTransform.sizeDelta = new Vector2(2f, 0f);
                rule.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            }
        }

        /// A soft drop shadow so a sheet reads as an object lying over the scene.
        public static void AddShadow(Graphic graphic, Vector2 distance)
        {
            if (graphic == null || graphic.GetComponent<Shadow>() != null) return;
            Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = distance;
        }

        /// A ledger's red double margin line, x units from the page's left edge. Returns both lines (to hide them).
        public static GameObject[] AddMarginLines(RectTransform page, float x)
        {
            string[] names = { "Margin A", "Margin B" };
            GameObject[] lines = new GameObject[2];
            for (int i = 0; i < 2; i++)
            {
                Transform existing = page.Find(names[i]);
                if (existing != null)
                {
                    lines[i] = existing.gameObject;
                    continue;
                }
                Image line = CreateImage(names[i], page, null, PTUITheme.MarginRed);
                line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Anchor(line.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(x + i * 4f, 30f), new Vector2(x + i * 4f + 1.5f, -24f));
                line.transform.SetSiblingIndex(1);                                         // just above the page, under the writing
                lines[i] = line.gameObject;
            }
            return lines;
        }

        /// The wooden rods of a scroll across the top and bottom of a document of the given width.
        public static void AddScrollRods(RectTransform panel, float width)
        {
            if (PTUITheme.ScrollRod == null) return;
            string[] names = { "Rod Bottom", "Rod Top" };
            for (int end = 0; end < 2; end++)
            {
                if (panel.Find(names[end]) != null) continue;
                Image rod = CreateImage(names[end], panel, null, Color.white);
                Slice(rod, PTUITheme.ScrollRod, 1.4f, Color.clear);
                rod.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Place(rod.rectTransform, new Vector2(0.5f, end), new Vector2(0f, end == 0 ? 6f : -6f), new Vector2(46f, width + 60f));
                rod.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);           // the art is vertical
            }
        }

        // ── The HUD prefab ────────────────────────────────────────────

        /// Turns the HUD prefab's texts into things in the world, laid out for a 1920x1080 reference: a note tacked up
        /// with the controls, a cloth banner for the status, hang tags for the company, the purse and the days served.
        /// Safe to run on an already-styled (baked) HUD: anything already on paper is left exactly as it is.
        /// Returns the banner's cloth, which is recoloured for battle.
        public static Image StyleHud(Transform hud)
        {
            if (hud == null) return null;
            const float edge = 24f;
            WrapInPlate(FindText(hud, "controlsText"), new Vector2(0f, 1f), new Vector2(edge, -edge), PTUITheme.Body, 20f, PTPlateStyle.Note);
            Image banner = WrapInPlate(FindText(hud, "battleText"), new Vector2(0.5f, 1f), new Vector2(0f, -8f), PTUITheme.Heading, 30f, PTPlateStyle.Banner);
            WrapInPlate(FindText(hud, "goldText"), new Vector2(0f, 0f), new Vector2(edge, edge), PTUITheme.Body, 28f, PTPlateStyle.Tag, PTUITheme.Coin);
            WrapInPlate(FindText(hud, "PartyNameText"), new Vector2(0f, 0f), new Vector2(edge, 132f), PTUITheme.Heading, 26f, PTPlateStyle.Tag);
            WrapInPlate(FindText(hud, "dayText"), new Vector2(1f, 0f), new Vector2(-edge, edge), PTUITheme.Heading, 24f, PTPlateStyle.Tag);
            return banner;
        }

        /// Puts an existing HUD text on an object that hugs it, pinned to a screen edge or corner (anchor doubles as the
        /// pivot, so a note in the top-left grows down and right as its text changes): a note tacked up, a cloth banner,
        /// a hang tag on twine, or a plain paper slip. Returns the object's paper (or cloth). If the text is already on
        /// one, that one is returned untouched.
        public static Image WrapInPlate(TextMeshProUGUI text, Vector2 anchor, Vector2 position, TMP_FontAsset font, float fontSize,
                                        PTPlateStyle style = PTPlateStyle.Slip, Sprite icon = null)
        {
            if (text == null) return null;
            RectTransform textRect = text.rectTransform;
            if (textRect.parent != null && textRect.parent.name == text.name + " Plate") return BackgroundOf(textRect.parent);

            GameObject plate = new GameObject(text.name + " Plate", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform rect = plate.GetComponent<RectTransform>();
            rect.SetParent(textRect.parent, false);
            rect.SetSiblingIndex(textRect.GetSiblingIndex());
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;

            HorizontalLayoutGroup layout = plate.GetComponent<HorizontalLayoutGroup>();
            Color textColor = TextColor;
            Image image;
            switch (style)
            {
                case PTPlateStyle.Note:
                    image = SetBackground(rect, PTUITheme.PaperPanel, 4f, PTUITheme.PaperFlat, true);
                    layout.padding = new RectOffset(30, 30, 30, 22);
                    rect.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
                    CreateOrnament("Pin", rect, PTUITheme.Pin, new Vector2(0.5f, 1f), new Vector2(0f, -13f), new Vector2(26f, 26f), 0f);
                    break;
                case PTPlateStyle.Banner:
                    image = SetBackground(rect, PTUITheme.Banner, 2.4f, PTUITheme.BannerTown, true);
                    if (PTUITheme.Banner != null) image.color = PTUITheme.BannerTown;
                    layout.padding = new RectOffset(110, 110, 8, 24);                      // the cloth band, between the tails
                    textColor = PTUITheme.Painted;
                    break;
                case PTPlateStyle.Tag:
                    image = SetBackground(rect, PTUITheme.Tag, 2.5f, PTUITheme.PaperFlat, true);
                    layout.padding = new RectOffset(26, 26, 30, 14);
                    Image twine = CreateImage("Twine", rect, null, PTUITheme.Twine);
                    twine.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    Anchor(twine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-1.5f, -14f), new Vector2(1.5f, 16f));
                    CreateOrnament("Eyelet", rect, PTUITheme.Eyelet, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(18f, 18f), 0f);
                    break;
                default:
                    image = SetBackground(rect, PTUITheme.PaperSlip, PlateBorderScale, PTUITheme.PaperFlat, true);
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
                iconSize.preferredWidth = fontSize * 1.2f;
                iconSize.preferredHeight = fontSize * 1.2f;
            }

            textRect.SetParent(rect, false);
            textRect.localScale = Vector3.one;                                             // the prefab scaled its texts; the plate sizes to the real text
            textRect.localRotation = Quaternion.identity;
            PTUITheme.SetFont(text, font);
            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.color = textColor;
            text.alignment = style == PTPlateStyle.Banner ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            return image;
        }

        /// The adventure log as a scroll: ink on parchment between two rolled ends, along the bottom of the screen.
        /// Undoes the prefab's stretched scales so the text is a normal size. Leaves an already-styled log alone.
        public static void StyleLog(ScrollRect scroll, TextMeshProUGUI entryTemplate)
        {
            if (scroll == null)
            {
                PTUITheme.SetFont(entryTemplate, PTUITheme.Body);
                return;
            }
            RectTransform log = scroll.GetComponent<RectTransform>();
            if (log.Find("Scroll End L") != null) return;                                   // already a scroll (baked)

            Transform backing = log.parent;                                                // the old tinted band behind the log: now an invisible full-screen holder
            RectTransform backingRect = backing as RectTransform;
            if (backingRect != null)
            {
                backingRect.localScale = Vector3.one;
                Anchor(backingRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Image band = backing.GetComponent<Image>();
                if (band != null)
                {
                    band.color = Color.clear;
                    band.raycastTarget = false;                                            // it covers the screen; it mustn't catch clicks
                }
            }
            log.localScale = Vector3.one;
            log.anchorMin = new Vector2(0.5f, 0f);
            log.anchorMax = new Vector2(0.5f, 0f);
            log.pivot = new Vector2(0.5f, 0f);
            log.anchoredPosition = new Vector2(0f, 18f);
            log.sizeDelta = new Vector2(860f, 150f);
            Slice(scroll.GetComponent<Image>(), PTUITheme.PaperPanel, 5f, PTUITheme.PaperFlat);

            RectTransform viewport = scroll.viewport;
            if (viewport != null)
            {
                viewport.localScale = Vector3.one;
                Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(26f, 12f), new Vector2(-38f, -12f));   // off the paper's torn edge
                Mask mask = viewport.GetComponent<Mask>();
                if (mask != null) mask.showMaskGraphic = false;
            }
            RectTransform content = scroll.content;
            if (content != null)
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(0f, content.sizeDelta.y);
                VerticalLayoutGroup lines = content.GetComponent<VerticalLayoutGroup>();
                if (lines != null)
                {
                    lines.childControlWidth = true;                                        // entries span the scroll, however wide it is
                    lines.childForceExpandWidth = true;
                    lines.spacing = 2f;
                }
            }
            if (entryTemplate != null)
            {
                entryTemplate.rectTransform.localScale = Vector3.one;
                PTUITheme.SetFont(entryTemplate, PTUITheme.Body);
                entryTemplate.enableAutoSizing = false;
                entryTemplate.fontSize = 19f;
                entryTemplate.alignment = TextAlignmentOptions.Left;
            }

            Scrollbar bar = scroll.verticalScrollbar;
            if (bar != null)
            {
                RectTransform barRect = bar.GetComponent<RectTransform>();
                if (barRect != null)
                {
                    barRect.localScale = Vector3.one;
                    barRect.anchoredPosition = new Vector2(-18f, 0f);
                    barRect.sizeDelta = new Vector2(10f, -28f);
                }
                Image track = bar.GetComponent<Image>();
                if (track != null) track.color = new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.12f);
                if (bar.handleRect != null && bar.handleRect.GetComponent<Image>() != null)
                {
                    bar.handleRect.GetComponent<Image>().color = new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.6f);
                }
            }

            if (PTUITheme.ScrollRod != null)                                               // rolled ends: the log is a scroll
            {
                string[] names = { "Scroll End L", "Scroll End R" };
                for (int end = 0; end < 2; end++)
                {
                    Image rod = CreateImage(names[end], log, null, Color.white);
                    Slice(rod, PTUITheme.ScrollRod, 2f, Color.clear);
                    Anchor(rod.rectTransform, new Vector2(end, 0f), new Vector2(end, 1f),
                           new Vector2(end == 0 ? -22f : -10f, -18f), new Vector2(end == 0 ? 10f : 22f, 18f));
                }
            }
        }

        private static TextMeshProUGUI FindText(Transform root, string name)
        {
            if (root.name == name) return root.GetComponent<TextMeshProUGUI>();
            for (int i = 0; i < root.childCount; i++)
            {
                TextMeshProUGUI found = FindText(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // ── Plumbing ──────────────────────────────────────────────────
        public static void EnsureEventSystem()                                             // buttons need an EventSystem to receive clicks
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// The decorative art of a container: a "Background" child stretched behind its content and ignored by its
        /// layout. Reuses the one that's there. Returns its Image.
        public static Image SetBackground(RectTransform container, Sprite sprite, float borderScale, Color flat, bool raycast = false)
        {
            Image image = BackgroundOf(container);
            if (image == null)
            {
                image = CreateImage(Background, container, null, Color.white);
                image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Anchor(image.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                image.transform.SetAsFirstSibling();
            }
            image.raycastTarget = raycast;
            Slice(image, sprite, borderScale, flat);
            return image;
        }

        public static Image BackgroundOf(Transform container)
        {
            Transform child = container != null ? container.Find(Background) : null;
            return child != null ? child.GetComponent<Image>() : null;
        }

        /// Sliced art on an Image (borderScale divides the sprite's border), or a flat colour if the art didn't load.
        public static void Slice(Image image, Sprite sprite, float borderScale, Color flat)
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
