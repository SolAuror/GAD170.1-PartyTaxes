using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// The paper tag tied over each character's head: a wax seal with their level, their name, HP as an inked bar
    /// and a thin line of XP. Hires also show what they cost and how they feel about it ("12g a day · Content");
    /// enemies get a ruddy wanted notice instead. Built at runtime in world space, it replaces the UI_PTInWorld
    /// canvas by taking over PTSoul's UI fields (so PTSoul.UpdateUI keeps working), follows its soul and faces the camera.
    /// </summary>
    public class PTWorldTag : MonoBehaviour
    {
        private const float Width = 300f;
        private const float Height = 150f;
        private const float MetresPerUnit = 0.0042f;                                       // about 1.25 m wide, the size of the old plate
        private const float Lift = 0.15f;                                                  // metres above where the old plate sat
        private const float DefaultHeight = 2.3f;                                          // metres above the soul when there's no old plate to go by

        private PTSoul soul;
        private Vector3 offset;
        private Canvas canvas;
        private CanvasGroup group;
        private TextMeshProUGUI detail;
        private string lastDetail;

        /// Gives a soul its tag (once). Does nothing for souls without the old in-world plate.
        public static PTWorldTag Attach(PTSoul soul)
        {
            if (soul == null || soul.nameText == null) return null;
            Vector3 offset = Vector3.up * DefaultHeight;
            if (soul.nameText.transform.IsChildOf(soul.transform))                         // its own UI_PTInWorld plate (not fields copied from a cloned soul)
            {
                Transform anchor = soul.xpSlider != null ? soul.xpSlider.transform : soul.nameText.transform;   // the bottom of the old plate
                offset = anchor.position - soul.transform.position + Vector3.up * Lift;
                Canvas old = soul.nameText.canvas;
                if (old != null) old.enabled = false;
            }

            GameObject root = new GameObject(soul.name + " Tag", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            PTWorldTag tag = root.AddComponent<PTWorldTag>();
            tag.soul = soul;
            tag.offset = offset;
            tag.Build(root.GetComponent<RectTransform>());
            soul.UpdateUI();
            tag.LateUpdate();
            return tag;
        }

        private void Build(RectTransform root)
        {
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            root.pivot = new Vector2(0.5f, 0f);                                            // hangs upwards from its anchor point
            root.sizeDelta = new Vector2(Width, Height);
            root.localScale = Vector3.one * MetresPerUnit;

            bool enemy = soul.isAdversary;

            Image twine = PTUIFactory.CreateImage("Twine", root, null, PTUITheme.Twine);  // the string it hangs from
            PTUIFactory.Anchor(twine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-1.5f, -22f), new Vector2(1.5f, 30f));

            Image paper = PTUIFactory.CreateImage("Paper", root, null, Color.white);
            PTUIFactory.Slice(paper, PTUITheme.Tag, 2f, PTUITheme.PaperFlat);
            if (enemy) paper.color = PTUITheme.BountyTint;
            PTUIFactory.Anchor(paper.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            if (enemy && PTUITheme.Stamp != null)                                          // a red border stamped on wanted notices
            {
                Image border = PTUIFactory.CreateImage("WantedBorder", root, null, Color.white);
                PTUIFactory.Slice(border, PTUITheme.Stamp, 3.5f, Color.clear);
                border.color = new Color(PTUITheme.StampRed.r, PTUITheme.StampRed.g, PTUITheme.StampRed.b, 0.55f);
                PTUIFactory.Anchor(border.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -10f));
            }

            Image eyelet = PTUIFactory.CreateImage("Eyelet", root, PTUITheme.Eyelet, PTUITheme.Eyelet != null ? Color.white : PTUITheme.Twine);
            PTUIFactory.Anchor(eyelet.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-12f, -34f), new Vector2(12f, -10f));

            Image seal = PTUIFactory.CreateImage("LevelSeal", root, PTUITheme.WaxDisc, PTUITheme.WaxDisc != null ? Color.white : PTUITheme.WaxFlat);
            seal.preserveAspect = true;
            PTUIFactory.Anchor(seal.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -112f), new Vector2(88f, -38f));
            TextMeshProUGUI level = Label("Level", seal.rectTransform, PTUITheme.Heading, 30f, TextAlignmentOptions.Center, PTUITheme.WaxText);
            PTUIFactory.Anchor(level.rectTransform, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -4f));

            TextMeshProUGUI title = Label("Name", root, PTUITheme.Heading, 30f, TextAlignmentOptions.Left, enemy ? PTUITheme.InkRed : PTUITheme.Ink);
            PTUIFactory.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(96f, -74f), new Vector2(-18f, -36f));

            detail = Label("Detail", root, PTUITheme.Body, 19f, TextAlignmentOptions.Left, new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.75f));
            detail.fontStyle = FontStyles.Italic;
            PTUIFactory.Anchor(detail.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(96f, -100f), new Vector2(-18f, -72f));

            Image box = PTUIFactory.CreateImage("HealthBox", root, null, Color.white);     // HP: red ink inside a hand-drawn box
            PTUIFactory.Slice(box, PTUITheme.InkBox, 1.5f, PTUITheme.InkOff);
            PTUIFactory.Anchor(box.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 26f), new Vector2(-88f, 52f));
            Slider health = Bar(box.rectTransform, PTUITheme.InkWash, PTUITheme.HealthInk, new Vector2(4f, 4f), new Vector2(-4f, -4f));

            TextMeshProUGUI hp = Label("HP", root, PTUITheme.Body, 20f, TextAlignmentOptions.Right, PTUITheme.Ink);
            PTUIFactory.Anchor(hp.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-86f, 20f), new Vector2(-18f, 56f));

            Image track = PTUIFactory.CreateImage("XpTrack", root, null, new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.15f));
            PTUIFactory.Anchor(track.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(28f, 14f), new Vector2(-92f, 19f));
            Slider xp = Bar(track.rectTransform, null, PTUITheme.XpInk, Vector2.zero, Vector2.zero);

            soul.nameText = title;                                                          // PTSoul.UpdateUI now writes to the tag
            soul.levelText = level;
            soul.healthText = hp;
            soul.healthSlider = health;
            soul.xpSlider = xp;
            soul.xpText = null;
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
            if (view != null) transform.rotation = view.transform.rotation;                // always face the camera
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
            text.fontSizeMin = 10f;
            text.fontSizeMax = size;
            return text;
        }

        /// A read-only Slider whose fill is an ink wash (or flat ink) inside the given rect.
        private static Slider Bar(RectTransform parent, Sprite fillSprite, Color ink, Vector2 insetMin, Vector2 insetMax)
        {
            GameObject area = new GameObject("Bar", typeof(RectTransform));
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
