using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// The paper tag tied over each character's head: a wax seal with their level, their name, HP as an inked bar
    /// and a thin line of XP. Hires also show what they cost and how they feel about it ("12g a day · Content");
    /// enemies get a ruddy wanted notice instead. It lives in world space, replaces the UI_PTInWorld canvas by taking
    /// over PTSoul's UI fields (so PTSoul.UpdateUI keeps working), follows its soul and faces the camera, and keeps
    /// the same size on screen however near or far the camera is, so it stays readable and never swamps the scene.
    /// Built at runtime, or copied from the baked "PT World Tag" prefab (Party Taxes > Bake UI) if there is one.
    /// </summary>
    public class PTWorldTag : MonoBehaviour
    {
        public const string PrefabName = "PT World Tag";
        private const float Width = 300f;
        private const float Height = 140f;

        [Tooltip("How tall the tag looks: a fraction of the screen's height.")]
        public float screenHeight = 0.085f;
        [Tooltip("Metres above where the old plate sat (or above the soul if it had none).")]
        public float lift = 0f;
        private const float DefaultHeight = 2.3f;                                          // metres above the soul when there's no old plate to go by

        private PTSoul soul;
        private Vector3 offset;
        private Canvas canvas;
        private CanvasGroup group;
        private TextMeshProUGUI detail;
        private TextMeshProUGUI title;
        private string lastDetail;

        /// Gives a soul its tag (once). Does nothing for souls without the old in-world plate.
        public static PTWorldTag Attach(PTSoul soul)
        {
            if (soul == null || soul.nameText == null) return null;
            Vector3 anchor = soul.transform.position + Vector3.up * DefaultHeight;
            if (soul.nameText.transform.IsChildOf(soul.transform))                         // its own UI_PTInWorld plate (not fields copied from a cloned soul)
            {
                anchor = (soul.xpSlider != null ? soul.xpSlider.transform : soul.nameText.transform).position;   // the bottom of the old plate
                Canvas old = soul.nameText.canvas;
                if (old != null) old.enabled = false;
            }

            PTWorldTag tag = PTUIBuilder.InstantiateBaked<PTWorldTag>(PrefabName);
            bool baked = tag != null;
            if (!baked) tag = new GameObject(PrefabName, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)).AddComponent<PTWorldTag>();
            tag.gameObject.name = soul.name + " Tag";
            tag.soul = soul;
            tag.offset = anchor - soul.transform.position + Vector3.up * tag.lift;
            tag.Build(new PTUIBuilder(baked ? tag.transform : null));
            tag.Bind();
            soul.UpdateUI();
            tag.LateUpdate();
            return tag;
        }

        /// Editor bake: a hero's tag with preview text.
        public static GameObject Bake()
        {
            PTWorldTag tag = new GameObject(PrefabName, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)).AddComponent<PTWorldTag>();
            tag.Build(new PTUIBuilder(null, true));
            tag.title.text = "Aelar the Elf";
            tag.detail.text = "13g a day  ·  Content";
            tag.transform.Find("Tag.Seal/Tag.Level").GetComponent<TextMeshProUGUI>().text = "1";
            tag.transform.Find("Tag.HP").GetComponent<TextMeshProUGUI>().text = "80/80";
            tag.transform.Find("Tag.Wanted").gameObject.SetActive(false);                  // shown on enemies only
            return tag.gameObject;
        }

        private void Build(PTUIBuilder ui)
        {
            RectTransform root = GetComponent<RectTransform>();
            canvas = GetComponent<Canvas>();
            group = GetComponent<CanvasGroup>();
            if (!ui.Rebinding)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                group.blocksRaycasts = false;
                group.interactable = false;
                root.pivot = new Vector2(0.5f, 0f);                                        // hangs upwards from its anchor point
                root.sizeDelta = new Vector2(Width, Height);
            }

            ui.Make("Tag.Twine", () =>                                                     // the string it hangs from
                PTUIFactory.Anchor(PTUIFactory.CreateImage("Tag.Twine", root, null, PTUITheme.Twine).rectTransform,
                                   new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-1.5f, -22f), new Vector2(1.5f, 12f)));
            ui.Make("Tag.Paper", () =>
            {
                Image paper = PTUIFactory.CreateImage("Tag.Paper", root, null, Color.white);
                PTUIFactory.Slice(paper, PTUITheme.Tag, 2f, PTUITheme.PaperFlat);
                PTUIFactory.Anchor(paper.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                return paper;
            });
            ui.Make("Tag.Wanted", () =>                                                     // a red border stamped on wanted notices
            {
                Image border = PTUIFactory.CreateImage("Tag.Wanted", root, null, Color.clear);
                PTUIFactory.Slice(border, PTUITheme.Stamp, 3.5f, Color.clear);
                if (PTUITheme.Stamp != null) border.color = new Color(PTUITheme.StampRed.r, PTUITheme.StampRed.g, PTUITheme.StampRed.b, 0.55f);
                PTUIFactory.Anchor(border.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -10f));
                return border;
            });
            ui.Make("Tag.Eyelet", () => PTUIFactory.Anchor(
                PTUIFactory.CreateImage("Tag.Eyelet", root, PTUITheme.Eyelet, PTUITheme.Eyelet != null ? Color.white : PTUITheme.Twine).rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-11f, -32f), new Vector2(11f, -10f)));

            Image seal = ui.Make("Tag.Seal", () =>
            {
                Image disc = PTUIFactory.CreateImage("Tag.Seal", root, PTUITheme.WaxDisc, PTUITheme.WaxDisc != null ? Color.white : PTUITheme.WaxFlat);
                disc.preserveAspect = true;
                PTUIFactory.Anchor(disc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -104f), new Vector2(84f, -30f));
                return disc;
            });
            TextMeshProUGUI level = ui.Make("Tag.Level", () =>
            {
                TextMeshProUGUI text = Label("Tag.Level", seal.rectTransform, PTUITheme.Heading, 32f, TextAlignmentOptions.Center, PTUITheme.WaxText);
                PTUIFactory.Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -6f));
                return text;
            });
            title = ui.Make("Tag.Name", () =>
            {
                TextMeshProUGUI text = Label("Tag.Name", root, PTUITheme.Heading, 34f, TextAlignmentOptions.Left, PTUITheme.Ink);
                PTUIFactory.Anchor(text.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(92f, -70f), new Vector2(-16f, -28f));
                return text;
            });
            detail = ui.Make("Tag.Detail", () =>
            {
                TextMeshProUGUI text = Label("Tag.Detail", root, PTUITheme.Body, 23f, TextAlignmentOptions.Left, new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.8f));
                text.fontStyle = FontStyles.Italic;
                PTUIFactory.Anchor(text.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(92f, -98f), new Vector2(-16f, -68f));
                return text;
            });

            Image box = ui.Make("Tag.HealthBox", () =>                                     // HP: red ink inside a hand-drawn box
            {
                Image outline = PTUIFactory.CreateImage("Tag.HealthBox", root, null, Color.white);
                PTUIFactory.Slice(outline, PTUITheme.InkBox, 1.5f, PTUITheme.InkOff);
                PTUIFactory.Anchor(outline.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(20f, 24f), new Vector2(-98f, 52f));
                return outline;
            });
            Slider health = ui.Make("Tag.Health", () => Bar("Tag.Health", box.rectTransform, PTUITheme.InkWash, PTUITheme.HealthInk, new Vector2(4f, 4f), new Vector2(-4f, -4f)));
            TextMeshProUGUI hp = ui.Make("Tag.HP", () =>
            {
                TextMeshProUGUI text = Label("Tag.HP", root, PTUITheme.Body, 25f, TextAlignmentOptions.Right, PTUITheme.Ink);
                PTUIFactory.Anchor(text.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-94f, 18f), new Vector2(-14f, 58f));
                return text;
            });
            Image track = ui.Make("Tag.XpTrack", () => PTUIFactory.Anchor(
                PTUIFactory.CreateImage("Tag.XpTrack", root, null, new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.15f)).rectTransform,
                Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 11f), new Vector2(-102f, 18f)).GetComponent<Image>());
            Slider xp = ui.Make("Tag.Xp", () => Bar("Tag.Xp", track.rectTransform, null, PTUITheme.XpInk, Vector2.zero, Vector2.zero));

            if (soul == null) return;                                                      // baking: no soul to take over
            soul.nameText = title;                                                         // PTSoul.UpdateUI now writes to the tag
            soul.levelText = level;
            soul.healthText = hp;
            soul.healthSlider = health;
            soul.xpSlider = xp;
            soul.xpText = null;
        }

        /// Hero tag or wanted notice.
        private void Bind()
        {
            bool enemy = soul.isAdversary;
            Transform paper = transform.Find("Tag.Paper");
            if (paper != null && enemy) paper.GetComponent<Image>().color = PTUITheme.BountyTint;
            Transform wanted = transform.Find("Tag.Wanted");
            if (wanted != null) wanted.gameObject.SetActive(enemy);
            if (enemy) title.color = PTUITheme.InkRed;
        }

        private void LateUpdate()
        {
            if (soul == null)                                                              // the soul was destroyed (dead and buried, or a new scene)
            {
                Destroy(gameObject);
                return;
            }
            bool shown = soul.gameObject.activeInHierarchy;                                // hidden while off-stage (e.g. tavern candidates)
            if (canvas.enabled != shown) canvas.enabled = shown;
            if (!shown) return;

            transform.position = soul.transform.position + offset;
            Camera view = Camera.main;
            if (view != null)
            {
                transform.rotation = view.transform.rotation;                              // always face the camera
                float distance = Mathf.Max(0.1f, Vector3.Dot(transform.position - view.transform.position, view.transform.forward));
                float visibleHeight = view.orthographic ? view.orthographicSize * 2f
                                                        : 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
                transform.localScale = Vector3.one * (visibleHeight * screenHeight / Height);   // same size on screen at any distance
            }
            group.alpha = soul.isAlive ? 1f : 0.55f;

            string line = DetailLine();
            if (line != lastDetail)
            {
                lastDetail = line;
                detail.text = line;
            }
        }

        private string DetailLine()
        {
            if (!soul.isAlive) return "Fallen";
            if (soul.isAdversary) return soul.isBoss ? "Wanted by the Crown" : "Wanted";
            PTEmployeeRecord job = soul.employment;
            string pay = job != null && job.OnLootShare ? "On a share" : soul.dailywages + "g a day";
            return job != null ? pay + "  ·  " + job.Mood : pay;
        }

        private static TextMeshProUGUI Label(string name, RectTransform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.GetComponent<RectTransform>().SetParent(parent, false);
            TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
            PTUITheme.SetFont(text, font);
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.enableAutoSizing = true;                                                  // long names shrink to fit
            text.fontSizeMin = Mathf.Round(size * 0.65f);
            text.fontSizeMax = size;
            return text;
        }

        /// A read-only Slider whose fill is an ink wash (or flat ink) inside the given rect.
        private static Slider Bar(string name, RectTransform parent, Sprite fillSprite, Color ink, Vector2 insetMin, Vector2 insetMax)
        {
            GameObject area = new GameObject(name, typeof(RectTransform));
            RectTransform areaRect = area.GetComponent<RectTransform>();
            areaRect.SetParent(parent, false);
            PTUIFactory.Anchor(areaRect, Vector2.zero, Vector2.one, insetMin, insetMax);

            Image fill = PTUIFactory.CreateImage("Fill", areaRect, null, ink);
            if (fillSprite != null)
            {
                fill.sprite = fillSprite;
                fill.type = Image.Type.Sliced;
                fill.pixelsPerUnitMultiplier = 3f;
            }
            PTUIFactory.Anchor(fill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Slider slider = area.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.interactable = false;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.fillRect = fill.rectTransform;
            return slider;
        }
    }
}
