using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// Clickable controls for the whole game, built at runtime by PTManager (no scene setup needed).
    /// Views: the main town actions, the tavern's hiring board, and the ledger (payroll, audits, firing).
    /// A caught thief forces a decision before anything else. Keyboard shortcuts keep working alongside it.
    /// PTManager calls Refresh() whenever its UI updates; every element re-reads its state from PTManager.
    /// </summary>
    public class PTActionBar : MonoBehaviour
    {
        public enum View { Main, Jobs, Tavern, Ledger }

        private const float PanelWidth   = 440f;
        private const float ButtonHeight = 40f;
        private const float SmallButtonHeight = 34f;

        private PTManager manager;
        private View view = View.Main;
        private PTSoul fireConfirm;                                                        // first click on Fire asks for confirmation
        private PTFinalLedgerPanel finalLedger;
        private readonly List<Action> refreshers = new List<Action>();

        /// Builds the action bar on its own overlay canvas and returns it.
        public static PTActionBar Create(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas("PT Action Bar", 10);
            PTActionBar bar = canvas.gameObject.AddComponent<PTActionBar>();
            bar.manager = manager;
            bar.Build(canvas);
            bar.Refresh();
            return bar;
        }

        public void ToggleTavern()
        {
            SetView(view == View.Tavern ? View.Main : View.Tavern);
        }

        public void ToggleJobs()
        {
            SetView(view == View.Jobs ? View.Main : View.Jobs);
        }

        private void SetView(View newView)
        {
            view = newView;
            fireConfirm = null;
            Refresh();
        }

        /// Re-reads costs and availability from PTManager and updates every element.
        public void Refresh()
        {
            if (manager == null) return;
            if (manager.State != PTManager.GameState.Town && view != View.Main)
            {
                view = View.Main;                                                          // sub-views only make sense in town
            }
            foreach (Action refresh in refreshers)
            {
                refresh();
            }
            if (manager.IsRunOver && finalLedger == null)
            {
                finalLedger = PTFinalLedgerPanel.Show(manager);                            // the run just ended: open the books
            }
        }

        // ── State helpers ──────────────────────────────────────────────
        private bool InTown()     => manager.State == PTManager.GameState.Town;
        private bool InCombat()   => manager.State == PTManager.GameState.Combat;
        private bool Confronting() => InTown() && manager.PendingConfrontation != null;
        private bool ShowView(View v) => InTown() && !Confronting() && view == v;
        private PTSoul MemberAt(int slot) => slot < manager.partyMembers.Count ? manager.partyMembers[slot] : null;
        private PTSoul CandidateAt(int slot) => slot < manager.TavernCandidates.Count ? manager.TavernCandidates[slot] : null;

        // ── Building ───────────────────────────────────────────────────
        private void Build(RectTransform canvas)
        {
            RectTransform panel = PTUIFactory.CreatePanel("Panel", canvas, new Vector2(1f, 1f), new Vector2(-24f, -24f), PanelWidth);

            TextMeshProUGUI header = PTUIFactory.CreateLabel("Header", panel, 28f, TextAlignmentOptions.Center, 38f);
            header.fontStyle = FontStyles.Bold;
            Bind(header.gameObject, () => true, () =>
            {
                switch (manager.State)
                {
                    case PTManager.GameState.Town:     header.text = "Day " + manager.daysElapsed + " of " + manager.RunLengthDays + " - Town"; break;
                    case PTManager.GameState.Combat:   header.text = "Battle! Round " + manager.CombatRound; break;
                    case PTManager.GameState.GameOver: header.text = "Game Over"; break;
                    case PTManager.GameState.Victory:  header.text = manager.RunOutcome + "!"; break;
                }
            });

            TextMeshProUGUI status = PTUIFactory.CreateLabel("Status", panel, 18f, TextAlignmentOptions.Center, 44f);
            Bind(status.gameObject, () => !manager.IsRunOver, () =>
                status.text = InCombat()
                    ? (manager.ActiveJob != null ? manager.ActiveJob.jobName + " (" + manager.JobPay(manager.ActiveJob) + "g)" : "Ambush!") +
                      "\n" + manager.stance + " stance  ·  Gold " + manager.gold
                    : manager.TributeStatus + "\nPayroll tonight " + manager.PayrollTonight + "g  ·  Average loyalty " +
                      Mathf.RoundToInt(manager.AverageLoyalty));

            BuildConfrontation(panel);
            BuildMainView(panel);
            BuildJobsView(panel);
            BuildTavernView(panel);
            BuildLedgerView(panel);
            BuildCombatView(panel);

            // Run over
            AddButton(panel, () => manager.IsRunOver, () => true, () => "Start a new run  [Esc]", manager.RestartRun);
        }

        private void BuildConfrontation(RectTransform panel)
        {
            TextMeshProUGUI caught = PTUIFactory.CreateLabel("Caught", panel, 20f, TextAlignmentOptions.Center, 52f);
            Bind(caught.gameObject, Confronting, () =>
            {
                PTSoul thief = manager.PendingConfrontation;
                caught.text = "Caught <b>" + thief.Name + "</b> with " + thief.employment.stolenUnrecovered + "g of your gold!";
            });
            AddButton(panel, Confronting, () => true,
                () => "Dock their pay  (+" + manager.PendingConfrontation.employment.stolenUnrecovered + "g, loyalty down)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.DockPay));
            AddButton(panel, Confronting, () => true, () => "Forgive them  (loyalty up, less greedy)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.Forgive));
            AddButton(panel, Confronting, () => true, () => "Fire them  (no severance)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.Fire));
        }

        private void BuildMainView(RectTransform panel)
        {
            Func<bool> main = () => ShowView(View.Main);
            AddButton(panel, main, () => !manager.hasFought,
                () => manager.hasFought ? "Already worked today" : "Job board: " + manager.JobBoard.Count + " contracts  [E]",
                () => SetView(View.Jobs));
            AddButton(panel, main, () => true,
                () => "Stance: " + manager.stance + "  (click to change)",
                manager.CycleStance);
            AddButton(panel, main, () => true,
                () => "Tavern: " + manager.TavernCandidates.Count + " for hire  [F]",
                () => SetView(View.Tavern));
            AddButton(panel, main, () => true,
                () => "Ledger: payroll & audits" + (manager.LastNightMissing > 0 && !manager.hasAudited ? "  (gold went missing!)" : ""),
                () => SetView(View.Ledger));
            AddButton(panel, main, () => !manager.hasHealed && manager.gold >= manager.HealCost,
                () => manager.hasHealed ? "Cleric is resting" : "Heal party  -" + manager.HealCost + "g  [Q]",
                manager.TryHeal);
            AddButton(panel, main, () => !manager.hasBeenBlessed && manager.gold >= manager.BlessCost,
                () => manager.hasBeenBlessed ? "Already blessed today" : "Bless party  -" + manager.BlessCost + "g  [1]",
                manager.TryBless);
            AddButton(panel, () => main() && manager.ResurrectionUnlocked,
                () => manager.FallenCount > 0 && !manager.PartyFull && manager.gold >= manager.CheapestResurrectionCost,
                () => manager.FallenCount == 0 ? "No fallen to raise" : "Resurrect  -" + manager.CheapestResurrectionCost + "g+  [3]",
                manager.TryResurrect);
            AddButton(panel, main, () => true, SleepLabel, manager.TrySleep);
        }

        private void BuildJobsView(RectTransform panel)
        {
            Func<bool> jobs = () => ShowView(View.Jobs);
            for (int i = 0; i < 4; i++)                                                    // three contracts, plus the Goblin King on the last day
            {
                int slot = i;
                Func<bool> visible = () => jobs() && slot < manager.JobBoard.Count;
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Job", panel, 18f, TextAlignmentOptions.Left, 46f);
                Bind(info.gameObject, visible, () =>
                {
                    PTJob job = manager.JobBoard[slot];
                    string risk = job.risk == JobRisk.High ? "<color=#FF8080>High risk</color>" : job.risk == JobRisk.Medium ? "Medium risk" : "Low risk";
                    info.text = "<b>" + job.jobName + "</b>  ·  " + manager.JobPay(job) + "g  ·  " + risk + "\n<size=80%>" + job.description + "</size>";
                });
                AddButton(panel, visible, () => !manager.hasFought,
                    () => manager.JobBoard[slot].kind == JobKind.Boss ? "Face the Goblin King (no retreat!)" : "Take this job",
                    () => manager.TryTakeJob(slot), SmallButtonHeight);
            }
            AddButton(panel, jobs, () => !manager.hasFought, () => "Rest day  (no pay, recover 40% HP)", manager.TryRestDay);
            AddButton(panel, jobs, () => true, () => "Back", () => SetView(View.Main));
        }

        private void BuildCombatView(RectTransform panel)
        {
            AddButton(panel, InCombat, () => !manager.HazardPaidThisRound && manager.gold >= manager.HazardPayCost,
                () => "Hazard pay: +25% damage  -" + manager.HazardPayCost + "g  [1]",
                manager.TryHazardPay);
            AddButton(panel, InCombat, () => !manager.PotionUsedThisRound && manager.gold >= manager.PotionCost && manager.PotionTarget != null,
                () => manager.PotionTarget != null ? "Potion for " + manager.PotionTarget.Name + "  -" + manager.PotionCost + "g  [Q]" : "Potion",
                manager.TryPotion);
            AddButton(panel, InCombat, () => !manager.BribedThisRound && manager.BribeTarget != null && manager.gold >= manager.BribeCost,
                () => manager.BribeTarget != null ? "Bribe " + manager.BribeTarget.Name + " to leave  -" + manager.BribeCost + "g  [3]" : "The King can't be bought",
                manager.TryBribe);
            AddButton(panel, InCombat, () => !manager.FocusOrderedThisRound && manager.FocusCandidate != null,
                () => manager.FocusCandidate != null ? "Focus fire on " + manager.FocusCandidate.Name + "  [F]" : "Focus fire",
                manager.TryFocusFire);
            AddButton(panel, InCombat, () => manager.CanRetreat,
                () => manager.CanRetreat ? "Sound the retreat  -" + manager.RetreatCost + "g, no pay  [R]" : "No retreat from the King!",
                manager.TryRetreat);
            AddButton(panel, InCombat, () => true,
                () => "Speed: " + (manager.Speed == PTManager.CombatSpeed.Normal ? "1x" : manager.Speed == PTManager.CombatSpeed.Fast ? "2x" : "Skip") + "  [Space]",
                manager.CycleCombatSpeed);
        }

        private string SleepLabel()
        {
            int due = manager.PayrollTonight + (manager.IsTributeDay ? manager.NextTributeAmount : 0);
            string what = manager.IsTributeDay ? "tribute + payroll " : "payroll ";
            return manager.gold >= due
                ? "Sleep  (" + what + due + "g)  [Space]"
                : "Sleep  (" + what + due + "g - you're short!)  [Space]";
        }

        private void BuildTavernView(RectTransform panel)
        {
            Func<bool> tavern = () => ShowView(View.Tavern);
            for (int i = 0; i < manager.TavernSize; i++)
            {
                int slot = i;
                Func<bool> visible = () => tavern() && CandidateAt(slot) != null;
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Candidate", panel, 18f, TextAlignmentOptions.Left, 46f);
                Bind(info.gameObject, visible, () =>
                {
                    PTSoul c = CandidateAt(slot);
                    info.text = "<b>" + c.Name + "</b>  L" + c.level + "\nHP " + c.maxHP + "  ATK " + c.attack + "  DEF " + c.defense +
                                "  ·  " + c.dailywages + "g/day  ·  " + (c.employment != null ? c.employment.GreedHint : "");
                });
                AddButton(panel, visible,
                    () => !manager.PartyFull && manager.gold >= manager.SigningFee(CandidateAt(slot)),
                    () => manager.PartyFull ? "Party is full" : "Hire  (signing fee " + manager.SigningFee(CandidateAt(slot)) + "g)",
                    () => manager.TryHire(slot), SmallButtonHeight);
            }
            TextMeshProUGUI empty = PTUIFactory.CreateLabel("Empty", panel, 18f, TextAlignmentOptions.Center, 30f);
            Bind(empty.gameObject, () => tavern() && manager.TavernCandidates.Count == 0, () => empty.text = "No one else is looking for work today.");
            AddButton(panel, tavern, () => true, () => "Back", () => SetView(View.Main));
        }

        private void BuildLedgerView(RectTransform panel)
        {
            Func<bool> ledger = () => ShowView(View.Ledger);
            TextMeshProUGUI summary = PTUIFactory.CreateLabel("LedgerSummary", panel, 18f, TextAlignmentOptions.Center, 28f);
            Bind(summary.gameObject, ledger, () => summary.text = manager.LastNightMissing > 0
                ? "<color=#FF8080>" + manager.LastNightMissing + "g went missing last night.</color> Audit a suspect?"
                : "The books balanced last night.");

            for (int i = 0; i < manager.maxPartyCount; i++)
            {
                int slot = i;
                Func<bool> visible = () => ledger() && MemberAt(slot) != null;
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Member", panel, 18f, TextAlignmentOptions.Left, 44f);
                Bind(info.gameObject, visible, () =>
                {
                    PTSoul m = MemberAt(slot);
                    PTEmployeeRecord job = m.employment;
                    info.text = "<b>" + m.Name + "</b>  L" + m.level + "  ·  " + m.dailywages + "g/day\n" +
                                (job != null ? job.Mood + " (" + job.loyalty + ")  ·  " + job.GreedHint +
                                               (job.nightsUnpaid > 0 ? "  ·  <color=#FF8080>unpaid " + job.nightsUnpaid + " night(s)</color>" : "") : "");
                });

                RectTransform row = PTUIFactory.CreateRow(panel, SmallButtonHeight);
                Bind(row.gameObject, visible, () => { });
                AddButton(row, visible, () => true,
                    () => MemberAt(slot) != null && MemberAt(slot).employment != null && !MemberAt(slot).employment.payTonight ? "Pay: <color=#FF8080>NO</color>" : "Pay: yes",
                    () => manager.TogglePay(MemberAt(slot)), SmallButtonHeight);
                AddButton(row, visible, () => !manager.hasAudited,
                    () => manager.hasAudited ? "Audited today" : "Audit",
                    () => { fireConfirm = null; manager.TryAudit(MemberAt(slot)); }, SmallButtonHeight);
                AddButton(row, visible, () => true,
                    () => fireConfirm != null && fireConfirm == MemberAt(slot) ? "<color=#FF8080>Sure?</color>" : "Fire  -" + manager.SeveranceFor(MemberAt(slot)) + "g",
                    () => OnFireClicked(MemberAt(slot)), SmallButtonHeight);
            }
            AddButton(panel, ledger, () => true, () => "Back", () => SetView(View.Main));
        }

        private void OnFireClicked(PTSoul member)
        {
            if (member == null) return;
            if (fireConfirm != member)
            {
                fireConfirm = member;                                                      // firing can't be undone: ask once
                Refresh();
                return;
            }
            fireConfirm = null;
            manager.TryFire(member);
        }

        // ── Element plumbing ───────────────────────────────────────────
        private void Bind(GameObject element, Func<bool> isVisible, Action update)
        {
            refreshers.Add(() =>
            {
                bool visible = isVisible();
                element.SetActive(visible);
                if (visible) update();
            });
        }

        private void AddButton(RectTransform parent, Func<bool> isVisible, Func<bool> isEnabled, Func<string> getLabel, Action onClick, float height = ButtonHeight)
        {
            TextMeshProUGUI label;
            Button button = PTUIFactory.CreateButton(parent, height, onClick, out label);
            Bind(button.gameObject, isVisible, () =>
            {
                bool enabled = isEnabled();
                button.interactable = enabled;
                label.text = getLabel();
                label.color = enabled ? PTUIFactory.TextColor : PTUIFactory.TextOff;
            });
        }
    }
}
