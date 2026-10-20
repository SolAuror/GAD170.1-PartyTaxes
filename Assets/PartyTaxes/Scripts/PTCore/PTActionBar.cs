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
        private PTRunSetupPanel runSetup;
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
            if (manager.State == PTManager.GameState.Setup && runSetup == null)
            {
                runSetup = PTRunSetupPanel.Show(manager);                                  // before the run: choose accusation, Audit Level and seed
            }
        }

        // ── State helpers ──────────────────────────────────────────────
        private bool InTown()     => manager.State == PTManager.GameState.Town;
        private bool InCombat()   => manager.State == PTManager.GameState.Combat;
        private bool Confronting() => InTown() && manager.PendingConfrontation != null;
        private bool Raising()     => InTown() && manager.PendingConfrontation == null && manager.PendingRaise != null;
        private bool Eventing()    => InTown() && manager.PendingConfrontation == null && manager.PendingRaise == null && manager.PendingEvent != null;
        private bool ShowView(View v) => InTown() && !manager.HasPendingDecision && view == v;
        private PTSoul MemberAt(int slot) => slot < manager.partyMembers.Count ? manager.partyMembers[slot] : null;
        private PTSoul CandidateAt(int slot) => slot < manager.TavernCandidates.Count ? manager.TavernCandidates[slot] : null;

        // ── Building ───────────────────────────────────────────────────
        private void Build(RectTransform canvas)
        {
            RectTransform panel = PTUIFactory.CreatePanel("Panel", canvas, new Vector2(1f, 1f), new Vector2(-24f, -24f), PanelWidth);

            TextMeshProUGUI header = PTUIFactory.CreateHeading("Header", panel, 30f, TextAlignmentOptions.Center, 38f);
            Bind(header.gameObject, () => true, () =>
            {
                switch (manager.State)
                {
                    case PTManager.GameState.Setup:    header.text = "Party Taxes"; break;
                    case PTManager.GameState.Town:     header.text = "Day " + manager.daysElapsed + " of " + manager.RunLengthDays + " - Town"; break;
                    case PTManager.GameState.Combat:   header.text = "Battle! Round " + manager.CombatRound; break;
                    case PTManager.GameState.GameOver: header.text = "Game Over"; break;
                    case PTManager.GameState.Victory:  header.text = manager.RunOutcome + "!"; break;
                }
            });

            TextMeshProUGUI status = PTUIFactory.CreateLabel("Status", panel, 18f, TextAlignmentOptions.Center, 44f);
            Bind(status.gameObject, () => !manager.IsRunOver && manager.State != PTManager.GameState.Setup, () =>
                status.text = InCombat()
                    ? (manager.ActiveJob != null ? manager.ActiveJob.jobName + " (" + manager.JobPay(manager.ActiveJob) + "g)" : "Ambush!") +
                      "\n" + manager.stance + " stance  ·  Gold " + manager.gold
                    : manager.TributeStatus + "\nPayroll tonight " + manager.PayrollTonight + "g  ·  Average loyalty " +
                      Mathf.RoundToInt(manager.AverageLoyalty));

            PTUIFactory.CreateDivider(panel, 18f);

            BuildConfrontation(panel);
            BuildRaisePrompt(panel);
            BuildEventPrompt(panel);
            BuildMainView(panel);
            BuildJobsView(panel);
            BuildTavernView(panel);
            BuildLedgerView(panel);
            BuildCombatView(panel);

            // Run over
            AddButton(panel, () => manager.IsRunOver, () => true, () => "Start a new run  [Esc]", manager.RestartRun, ButtonHeight, PTButtonStyle.Wax);
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

        private void BuildRaisePrompt(RectTransform panel)
        {
            TextMeshProUGUI ask = PTUIFactory.CreateLabel("Raise", panel, 20f, TextAlignmentOptions.Center, 52f);
            Bind(ask.gameObject, Raising, () =>
            {
                PTSoul member = manager.PendingRaise;
                ask.text = "<b>" + member.Name + "</b> reached level " + member.level + " and wants a raise:\n" +
                           member.dailywages + "g to " + member.employment.requestedWage + "g a day.";
            });
            AddButton(panel, Raising, () => true, () => "Accept  (+5 loyalty)", () => manager.ResolveRaise(PTManager.RaiseChoice.Accept));
            AddButton(panel, Raising, () => true,
                () => "Counter-offer half  (" + Mathf.RoundToInt(manager.CounterChance(manager.PendingRaise) * 100) + "% chance, else -10 loyalty)",
                () => manager.ResolveRaise(PTManager.RaiseChoice.Counter));
            AddButton(panel, Raising, () => true, () => "Refuse  (-15 loyalty)", () => manager.ResolveRaise(PTManager.RaiseChoice.Refuse));
        }

        private void BuildEventPrompt(RectTransform panel)
        {
            TextMeshProUGUI story = PTUIFactory.CreateLabel("Event", panel, 20f, TextAlignmentOptions.Center, 78f);
            Bind(story.gameObject, Eventing, () =>
                story.text = "<b>" + manager.PendingEvent.title + "</b>\n" + manager.EventText(manager.PendingEvent.text));
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Func<bool> visible = () => Eventing() && index < manager.PendingEvent.choices.Length;
                AddButton(panel, visible, () => manager.CanChooseEventOption(index), () =>
                {
                    PTEventChoice choice = manager.PendingEvent.choices[index];
                    string cost = choice.GoldCost > 0 ? "  -" + choice.GoldCost + "g" : "";
                    string odds = choice.Chance < 1f ? "  (" + Mathf.RoundToInt(choice.Chance * 100) + "% chance)" : "";
                    return manager.EventText(choice.label) + cost + odds;
                }, () => manager.ResolveEvent(index));
            }
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
            AddButton(panel, main, () => true, SleepLabel, manager.TrySleep, ButtonHeight, PTButtonStyle.Wax);
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
                    string risk = job.risk == JobRisk.High ? "<color=" + PTUITheme.RedInk + ">High risk</color>" : job.risk == JobRisk.Medium ? "Medium risk" : "Low risk";
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
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Candidate", panel, 18f, TextAlignmentOptions.Left, 66f);
                Bind(info.gameObject, visible, () =>
                {
                    PTSoul c = CandidateAt(slot);
                    PTEmployeeRecord job = c.employment;
                    info.text = "<b>" + c.Name + "</b>  L" + c.level + "  ·  " + c.dailywages + "g/day\nHP " + c.maxHP + "  ATK " + c.attack + "  DEF " + c.defense +
                                "  ·  " + (job != null ? job.GreedHint : "") +
                                (job != null && job.traits.Count > 0 ? "\n<color=" + PTUITheme.GoldInk + ">" + job.TraitNames + "</color>" : "");
                });
                RectTransform row = PTUIFactory.CreateRow(panel, SmallButtonHeight);
                Bind(row.gameObject, visible, () => { });
                AddContractButton(row, visible, slot, ContractType.DayWage, c => "Wage  " + manager.SigningFee(c, ContractType.DayWage) + "g");
                AddContractButton(row, visible, slot, ContractType.LootShare,
                    c => "Share " + Mathf.RoundToInt(manager.LootSharePercent * 100) + "%  " + manager.SigningFee(c, ContractType.LootShare) + "g");
                AddContractButton(row, visible, slot, ContractType.SigningBonus,
                    c => "Bonus  " + manager.SigningFee(c, ContractType.SigningBonus) + "g, " + manager.ContractWage(c, ContractType.SigningBonus) + "g/day");
            }
            TextMeshProUGUI hint = PTUIFactory.CreateLabel("ContractHint", panel, 15f, TextAlignmentOptions.Center, 34f);
            Bind(hint.gameObject, () => tavern() && manager.TavernCandidates.Count > 0, () =>
                hint.text = "<color=" + PTUITheme.FadedInk + ">Wage: pay daily. Share: no wage, a cut of every job. Bonus: big fee now, 60% wage for good.</color>");
            TextMeshProUGUI empty = PTUIFactory.CreateLabel("Empty", panel, 18f, TextAlignmentOptions.Center, 30f);
            Bind(empty.gameObject, () => tavern() && manager.TavernCandidates.Count == 0, () => empty.text = "No one else is looking for work today.");
            AddButton(panel, tavern, () => true, () => "Back", () => SetView(View.Main));
        }

        private void AddContractButton(RectTransform row, Func<bool> visible, int slot, ContractType contract, Func<PTSoul, string> label)
        {
            AddButton(row, visible,
                () => !manager.PartyFull && CandidateAt(slot) != null && manager.gold >= manager.SigningFee(CandidateAt(slot), contract),
                () => CandidateAt(slot) == null ? "" : manager.PartyFull ? "Full" : label(CandidateAt(slot)),
                () => manager.TryHire(slot, contract), SmallButtonHeight);
        }

        private void BuildLedgerView(RectTransform panel)
        {
            Func<bool> ledger = () => ShowView(View.Ledger);
            TextMeshProUGUI summary = PTUIFactory.CreateLabel("LedgerSummary", panel, 18f, TextAlignmentOptions.Center, 28f);
            Bind(summary.gameObject, ledger, () => summary.text = manager.LastNightMissing > 0
                ? "<color=" + PTUITheme.RedInk + ">" + manager.LastNightMissing + "g went missing last night.</color> Audit a suspect?"
                : "The books balanced last night.");

            for (int i = 0; i < manager.maxPartyCount; i++)
            {
                int slot = i;
                Func<bool> visible = () => ledger() && MemberAt(slot) != null;
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Member", panel, 18f, TextAlignmentOptions.Left, 62f);
                Bind(info.gameObject, visible, () =>
                {
                    PTSoul m = MemberAt(slot);
                    PTEmployeeRecord job = m.employment;
                    string pay = job != null && job.OnLootShare ? Mathf.RoundToInt(manager.LootSharePercent * 100) + "% share" : m.dailywages + "g/day";
                    info.text = "<b>" + m.Name + "</b>  L" + m.level + "  ·  " + pay + "\n" +
                                (job != null ? job.Mood + " (" + job.loyalty + ")  ·  " + job.GreedHint +
                                               (job.nightsUnpaid > 0 ? "  ·  <color=" + PTUITheme.RedInk + ">unpaid " + job.nightsUnpaid + " night(s)</color>" : "") +
                                               (job.traits.Count > 0 ? "\n<color=" + PTUITheme.GoldInk + ">" + job.TraitNames + "</color>" : "") : "");
                });

                RectTransform row = PTUIFactory.CreateRow(panel, SmallButtonHeight);
                Bind(row.gameObject, visible, () => { });
                AddButton(row, visible, () => true,
                    () => MemberAt(slot) != null && MemberAt(slot).employment != null && MemberAt(slot).employment.OnLootShare ? "On share" :
                          MemberAt(slot) != null && MemberAt(slot).employment != null && !MemberAt(slot).employment.payTonight ? "Pay: <color=" + PTUITheme.RedInk + ">NO</color>" : "Pay: yes",
                    () => manager.TogglePay(MemberAt(slot)), SmallButtonHeight);
                AddButton(row, visible, () => !manager.hasAudited,
                    () => manager.hasAudited ? "Audited today" : "Audit",
                    () => { fireConfirm = null; manager.TryAudit(MemberAt(slot)); }, SmallButtonHeight);
                AddButton(row, visible, () => manager.CanBeFired(MemberAt(slot)),
                    () => !manager.CanBeFired(MemberAt(slot)) ? "Can't fire" :
                          fireConfirm != null && fireConfirm == MemberAt(slot) ? "<color=" + PTUITheme.RedInk + ">Sure?</color>" : "Fire  -" + manager.SeveranceFor(MemberAt(slot)) + "g",
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

        private void AddButton(RectTransform parent, Func<bool> isVisible, Func<bool> isEnabled, Func<string> getLabel, Action onClick,
                               float height = ButtonHeight, PTButtonStyle style = PTButtonStyle.Paper)
        {
            TextMeshProUGUI label;
            Button button = PTUIFactory.CreateButton(parent, height, onClick, out label, style);
            Bind(button.gameObject, isVisible, () =>
            {
                bool enabled = isEnabled();
                button.interactable = enabled;
                label.text = getLabel();
                label.color = PTUITheme.ButtonText(style, enabled);
            });
        }
    }
}
