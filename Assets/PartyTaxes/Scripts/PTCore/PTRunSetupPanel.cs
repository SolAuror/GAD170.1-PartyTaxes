using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// The start-of-run screen: pick an accusation (starting archetype), an Audit Level (difficulty)
    /// and a random or daily seed, then begin. Locked options show how to unlock them.
    /// Built at runtime by PTActionBar while PTManager is in the Setup state.
    /// </summary>
    public class PTRunSetupPanel : MonoBehaviour
    {
        private const float PanelWidth = 760f;

        private PTManager manager;
        private Accusation accusation = Accusation.Clerk;
        private int auditLevel = 1;
        private bool dailyRun = false;
        private readonly List<Action> refreshers = new List<Action>();

        public static PTRunSetupPanel Show(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas("PT Run Setup", 30, true);
            PTRunSetupPanel setup = canvas.gameObject.AddComponent<PTRunSetupPanel>();
            setup.manager = manager;
            setup.auditLevel = PTRunRules.MaxAuditLevel;                                   // default to the hardest unlocked level
            setup.Build(canvas);
            setup.Refresh();
            return setup;
        }

        private void Build(RectTransform canvas)
        {
            RectTransform panel = PTUIFactory.CreatePanel("Panel", canvas, new Vector2(0.5f, 0.5f), Vector2.zero, PanelWidth, 44, 8f);
            PTUIFactory.CreateOrnament("Seal", panel, PTUITheme.WaxSeal, new Vector2(1f, 1f), new Vector2(-34f, -30f), new Vector2(124f, 124f), -14f);

            TextMeshProUGUI title = PTUIFactory.CreateTitle("Title", panel, 72f, 80f);
            title.text = "Party Taxes";
            PTUIFactory.CreateDivider(panel);
            TextMeshProUGUI pitch = PTUIFactory.CreateLabel("Pitch", panel, 21f, TextAlignmentOptions.Center, 52f);
            pitch.text = "<i>Convicted of embezzlement and exiled to the goblin frontier. Hire heroes, pay the Queen, survive 28 days.</i>";

            TextMeshProUGUI accusationHeader = PTUIFactory.CreateHeading("AccusationHeader", panel, 26f, TextAlignmentOptions.Left, 34f);
            accusationHeader.text = "What were you accused of?";
            RectTransform accusationRow = PTUIFactory.CreateRow(panel, 44f);
            foreach (Accusation option in (Accusation[])Enum.GetValues(typeof(Accusation)))
            {
                Accusation choice = option;
                AddButton(accusationRow, () => PTRunRules.IsUnlocked(choice),
                    () => (accusation == choice ? "<b>> " : "") + PTRunRules.Name(choice).Replace("The ", "") + (accusation == choice ? "</b>" : "") +
                          (PTRunRules.IsUnlocked(choice) ? "" : " (locked)"),
                    () => { accusation = choice; Refresh(); });
            }
            TextMeshProUGUI accusationText = PTUIFactory.CreateLabel("AccusationText", panel, 19f, TextAlignmentOptions.Left, 56f);
            refreshers.Add(() => accusationText.text = PTRunRules.Description(accusation) + LockedHints());

            TextMeshProUGUI levelHeader = PTUIFactory.CreateHeading("LevelHeader", panel, 26f, TextAlignmentOptions.Left, 34f);
            levelHeader.text = "Audit Level";
            RectTransform levelRow = PTUIFactory.CreateRow(panel, 40f);
            for (int level = 1; level <= PTRunRules.MAX_AUDIT_LEVEL; level++)
            {
                int choice = level;
                AddButton(levelRow, () => choice <= PTRunRules.MaxAuditLevel,
                    () => (auditLevel == choice ? "<b>> " : "") + choice + (auditLevel == choice ? "</b>" : ""),
                    () => { auditLevel = choice; Refresh(); });
            }
            TextMeshProUGUI levelText = PTUIFactory.CreateLabel("LevelText", panel, 19f, TextAlignmentOptions.Left, 56f);
            refreshers.Add(() =>
            {
                List<string> rules = new List<string>();
                for (int level = 1; level <= auditLevel; level++) rules.Add(PTRunRules.LevelDescription(level));
                levelText.text = string.Join(" ", rules) + "  <size=80%>(score x" + PTRunRules.ScoreMultiplier(auditLevel).ToString("0.##") +
                                 (PTRunRules.MaxAuditLevel < PTRunRules.MAX_AUDIT_LEVEL ? "; win to unlock the next level" : "") + ")</size>";
            });

            RectTransform seedRow = PTUIFactory.CreateRow(panel, 40f);
            AddButton(seedRow, () => true, () => (dailyRun ? "" : "<b>> ") + "Random run" + (dailyRun ? "" : "</b>"), () => { dailyRun = false; Refresh(); });
            AddButton(seedRow, () => true, () => (dailyRun ? "<b>> " : "") + "Daily run (" + PTRunRules.DailySeed() + ")" + (dailyRun ? "</b>" : ""),
                () => { dailyRun = true; Refresh(); });

            TextMeshProUGUI record = PTUIFactory.CreateLabel("Record", panel, 18f, TextAlignmentOptions.Center, 28f);
            refreshers.Add(() => record.text = "Wins " + PTRunRules.Wins + "  ·  Best day " + PTRunRules.BestDay + "  ·  Thieves caught " +
                                               PTRunRules.ThievesCaught + "  ·  Best score " + PTRunRules.BestScore);

            PTUIFactory.CreateDivider(panel);
            TextMeshProUGUI beginLabel;
            PTUIFactory.CreateButton(panel, 56f, Begin, out beginLabel, PTButtonStyle.Wax);
            beginLabel.text = "<b>Begin your sentence</b>";
        }

        private string LockedHints()
        {
            List<string> hints = new List<string>();
            foreach (Accusation option in (Accusation[])Enum.GetValues(typeof(Accusation)))
            {
                if (!PTRunRules.IsUnlocked(option)) hints.Add(PTRunRules.Name(option).Replace("The ", "") + ": " + PTRunRules.UnlockCondition(option));
            }
            return hints.Count == 0 ? "" : "\n<size=80%><color=" + PTUITheme.FadedInk + ">Locked: " + string.Join(" · ", hints) + "</color></size>";
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

        private void AddButton(RectTransform parent, Func<bool> isEnabled, Func<string> getLabel, Action onClick)
        {
            TextMeshProUGUI label;
            Button button = PTUIFactory.CreateButton(parent, 40f, onClick, out label);
            refreshers.Add(() =>
            {
                bool enabled = isEnabled();
                button.interactable = enabled;
                label.text = getLabel();
                label.color = enabled ? PTUIFactory.TextColor : PTUIFactory.TextOff;
            });
        }
    }
}
