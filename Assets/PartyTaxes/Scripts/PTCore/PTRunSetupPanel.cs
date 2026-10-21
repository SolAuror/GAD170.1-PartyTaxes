using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// The start-of-run screen, written as the Queen's writ of sentencing: pick the charge (starting archetype),
    /// the severity of the audit (Audit Level, difficulty) and a random or daily case (seed), then sign it.
    /// Locked options show how to unlock them.
    /// Shown by PTActionBar while PTManager is in the Setup state: built at runtime, or re-bound to a baked copy
    /// (Party Taxes > Bake UI) so it can be edited in the scene.
    /// </summary>
    public class PTRunSetupPanel : MonoBehaviour
    {
        public const string PrefabName = "PT Run Setup";
        private const float PanelWidth = 760f;
        private static readonly string[] Roman = { "I", "II", "III", "IV", "V" };

        private PTManager manager;
        private PTUIBuilder ui;
        private Accusation accusation = Accusation.Clerk;
        private int auditLevel = 1;
        private bool dailyRun = false;
        private readonly List<Action> refreshers = new List<Action>();

        public static PTRunSetupPanel Show(PTManager manager)
        {
            PTRunSetupPanel setup = PTUIBuilder.FindBaked<PTRunSetupPanel>(PrefabName);
            bool baked = setup != null;
            RectTransform canvas = baked ? setup.GetComponent<RectTransform>() : PTUIFactory.CreateCanvas(PrefabName, 30, true);
            if (!baked) setup = canvas.gameObject.AddComponent<PTRunSetupPanel>();
            setup.manager = manager;
            setup.ui = new PTUIBuilder(baked ? canvas : null);
            setup.auditLevel = PTRunRules.MaxAuditLevel;                                   // default to the hardest unlocked level
            setup.Build(canvas);
            setup.Refresh();
            return setup;
        }

        /// Editor bake: the writ with preview text, switched off (it shows itself when a run is set up).
        public static GameObject Bake(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas(PrefabName, 30, true);
            PTRunSetupPanel setup = canvas.gameObject.AddComponent<PTRunSetupPanel>();
            setup.manager = manager;
            setup.ui = new PTUIBuilder(null, true);
            setup.Build(canvas);
            return canvas.gameObject;
        }

        private void Build(RectTransform canvas)
        {
            RectTransform panel = ui.Make("Writ", () =>
            {
                RectTransform writ = PTUIFactory.CreatePanel("Writ", canvas, new Vector2(0.5f, 0.5f), Vector2.zero, PanelWidth, new RectOffset(52, 52, 54, 54), 6f);
                PTUIFactory.AddShadow(PTUIFactory.BackgroundOf(writ), new Vector2(8f, -8f));
                PTUIFactory.AddScrollRods(writ, PanelWidth);                               // rolled top and bottom: it's a scroll
                PTUIFactory.CreateOrnament("Seal", writ, PTUITheme.WaxSeal, new Vector2(1f, 1f), new Vector2(-44f, -46f), new Vector2(116f, 116f), -14f);
                return writ;
            });

            ui.Make("Writ.Title", () =>
            {
                TextMeshProUGUI title = PTUIFactory.CreateTitle("Writ.Title", panel, PTUITheme.SizeTitle, 78f);
                title.text = "Party Taxes";
                return title;
            });
            ui.Make("Writ.Subtitle", () =>
            {
                TextMeshProUGUI subtitle = PTUIFactory.CreateHeading("Writ.Subtitle", panel, 22f, TextAlignmentOptions.Center);
                subtitle.text = "A Royal Writ of Sentencing";
                return subtitle;
            });
            ui.Make("Writ.Divider", () => PTUIFactory.CreateDivider(panel));
            ui.Make("Writ.Decree", () =>
            {
                TextMeshProUGUI decree = PTUIFactory.CreateLabel("Writ.Decree", panel, 20f, TextAlignmentOptions.Center);
                decree.text = "<i>By order of Her Majesty the Queen: the accused, found wanting in the Crown's accounts, is exiled to the goblin frontier, " +
                              "there to hire what heroes the purse allows, pay the Queen her tribute, and survive twenty-eight days.</i>";
                return decree;
            });

            ui.Make("Writ.ChargeHeader", () =>
            {
                TextMeshProUGUI header = PTUIFactory.CreateHeading("Writ.ChargeHeader", panel, PTUITheme.SizeSubhead, TextAlignmentOptions.Left);
                header.text = "The charge";
                return header;
            });
            RectTransform accusationRow = ui.Make("Writ.Charges", () => PTUIFactory.CreateRow(panel, 46f));
            foreach (Accusation option in (Accusation[])Enum.GetValues(typeof(Accusation)))
            {
                Accusation choice = option;
                AddButton("Writ.Charge." + option, accusationRow, 46f, () => PTRunRules.IsUnlocked(choice),
                    () => (accusation == choice ? "<b>> " : "") + PTRunRules.Name(choice).Replace("The ", "") + (accusation == choice ? "</b>" : "") +
                          (PTRunRules.IsUnlocked(choice) ? "" : " (locked)"),
                    () => { accusation = choice; Refresh(); });
            }
            TextMeshProUGUI accusationText = ui.Make("Writ.ChargeText", () => PTUIFactory.CreateLabel("Writ.ChargeText", panel, 19f, TextAlignmentOptions.Left));
            ui.Preview(accusationText, () => PTRunRules.Description(accusation) + LockedHints(), "Charge description");
            refreshers.Add(() => accusationText.text = PTRunRules.Description(accusation) + LockedHints());

            ui.Make("Writ.AuditHeader", () =>
            {
                TextMeshProUGUI header = PTUIFactory.CreateHeading("Writ.AuditHeader", panel, PTUITheme.SizeSubhead, TextAlignmentOptions.Left);
                header.text = "Severity of the audit";
                return header;
            });
            RectTransform levelRow = ui.Make("Writ.Audits", () => PTUIFactory.CreateRow(panel, 42f));
            for (int level = 1; level <= PTRunRules.MAX_AUDIT_LEVEL; level++)
            {
                int choice = level;
                AddButton("Writ.Audit." + level, levelRow, 42f, () => choice <= PTRunRules.MaxAuditLevel,
                    () => (auditLevel == choice ? "<b>> " : "") + Roman[choice - 1] + (auditLevel == choice ? "</b>" : ""),
                    () => { auditLevel = choice; Refresh(); });
            }
            TextMeshProUGUI levelText = ui.Make("Writ.AuditText", () => PTUIFactory.CreateLabel("Writ.AuditText", panel, 19f, TextAlignmentOptions.Left));
            ui.Preview(levelText, LevelText, "Audit description");
            refreshers.Add(() => levelText.text = LevelText());

            RectTransform seedRow = ui.Make("Writ.Cases", () => PTUIFactory.CreateRow(panel, 42f));
            AddButton("Writ.Case.Random", seedRow, 42f, () => true, () => (dailyRun ? "" : "<b>> ") + "Random case" + (dailyRun ? "" : "</b>"),
                () => { dailyRun = false; Refresh(); });
            AddButton("Writ.Case.Daily", seedRow, 42f, () => true, () => (dailyRun ? "<b>> " : "") + "Today's case (daily " + PTRunRules.DailySeed() + ")" + (dailyRun ? "</b>" : ""),
                () => { dailyRun = true; Refresh(); });

            TextMeshProUGUI record = ui.Make("Writ.Record", () => PTUIFactory.CreateLabel("Writ.Record", panel, 18f, TextAlignmentOptions.Center));
            Func<string> recordText = () => "Your record: wins " + PTRunRules.Wins + "  ·  best day " + PTRunRules.BestDay + "  ·  thieves caught " +
                                            PTRunRules.ThievesCaught + "  ·  best score " + PTRunRules.BestScore;
            ui.Preview(record, recordText, "Your record");
            refreshers.Add(() => record.text = recordText());

            ui.Make("Writ.Divider2", () => PTUIFactory.CreateDivider(panel));
            Button begin = ui.Make("Writ.Begin", () =>
            {
                Button button = PTUIFactory.CreateButton(panel, 56f, PTButtonStyle.Wax, 24f);
                PTUIFactory.LabelOf(button).text = "<b>Sign the writ and begin your sentence</b>";
                return button;
            });
            PTUIFactory.OnClick(begin, Begin);
        }

        private string LevelText()
        {
            List<string> rules = new List<string>();
            for (int level = 1; level <= auditLevel; level++) rules.Add(PTRunRules.LevelDescription(level));
            return string.Join(" ", rules) + "  <size=85%>(score x" + PTRunRules.ScoreMultiplier(auditLevel).ToString("0.##") +
                   (PTRunRules.MaxAuditLevel < PTRunRules.MAX_AUDIT_LEVEL ? "; win to unlock the next level" : "") + ")</size>";
        }

        private string LockedHints()
        {
            List<string> hints = new List<string>();
            foreach (Accusation option in (Accusation[])Enum.GetValues(typeof(Accusation)))
            {
                if (!PTRunRules.IsUnlocked(option)) hints.Add(PTRunRules.Name(option).Replace("The ", "") + ": " + PTRunRules.UnlockCondition(option));
            }
            return hints.Count == 0 ? "" : "\n<size=85%><color=" + PTUITheme.FadedInk + ">Locked: " + string.Join(" · ", hints) + "</color></size>";
        }

        private void Begin()
        {
            int seed = dailyRun ? PTRunRules.DailySeed() : PTRunRules.RandomSeed();
            manager.BeginRun(accusation, auditLevel, seed, dailyRun);
            Destroy(gameObject);
        }

        private void Refresh()
        {
            foreach (Action refresh in refreshers) refresh();
        }

        private void AddButton(string id, RectTransform parent, float height, Func<bool> isEnabled, Func<string> getLabel, Action onClick)
        {
            Button button = ui.Make(id, () => PTUIFactory.CreateButton(parent, height, PTButtonStyle.Paper, 19f));
            TextMeshProUGUI label = PTUIFactory.LabelOf(button);
            Color on = label.color;
            Color off = new Color(on.r, on.g, on.b, on.a * 0.42f);
            PTUIFactory.OnClick(button, onClick);
            ui.Preview(label, getLabel, id);
            refreshers.Add(() =>
            {
                bool enabled = isEnabled();
                button.interactable = enabled;
                label.text = getLabel();
                label.color = enabled ? on : off;
            });
        }
    }
}
