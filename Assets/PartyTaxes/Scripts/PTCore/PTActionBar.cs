using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// Clickable controls for the whole game, built at runtime by PTManager (no scene setup needed), drawn as the
    /// accountant's own things. In town, a wooden signpost picks where you are: the Town Square (your ledger: each
    /// action is a line entry, hotkey in the margin, cost in the debit column), the Notice Board (contracts tacked to
    /// cork), the Tavern (people seeking work, on the same board) and the Counting House (payroll, audits, firing).
    /// In battle the ledger lists field expenses. Decisions arrive as documents: a theft report, a petition for a
    /// raise, a letter about the night's events. Keyboard shortcuts keep working alongside it.
    /// PTManager calls Refresh() whenever its UI updates; every element re-reads its state from PTManager.
    /// </summary>
    public class PTActionBar : MonoBehaviour
    {
        public enum View { Main, Jobs, Tavern, Ledger }

        private const float PanelWidth   = 470f;
        private const float EntryHeight  = 36f;
        private const float ButtonHeight = 40f;
        private const float SmallButtonHeight = 32f;
        private const float MarginX = 82f;                                                 // the ledger's margin line (page padding + hotkey column)
        private static readonly Color CorkFlat = new Color(0.55f, 0.38f, 0.24f, 1f);

        private PTManager manager;
        private View view = View.Main;
        private PTSoul fireConfirm;                                                        // first click on Fire asks for confirmation
        private PTFinalLedgerPanel finalLedger;
        private PTRunSetupPanel runSetup;
        private readonly List<Action> refreshers = new List<Action>();

        private Image surface;                                                             // the ledger page, or the cork of the notice board
        private GameObject[] marginLines;
        private Image titleSheet;                                                          // the header's own paper, only shown on the board
        private Shadow titleShadow;
        private GameObject titlePin;
        private bool? onBoard;

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
            bool board = ShowView(View.Jobs) || ShowView(View.Tavern);
            SetSurface(board);
            bool ruled = !board && (ShowView(View.Main) || InCombat());                    // the margin only belongs on pages of entries
            foreach (GameObject line in marginLines)
            {
                if (line.activeSelf != ruled) line.SetActive(ruled);
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

        /// Ledger page or notice board. On the board the header gets its own tacked-up sheet so it stays readable.
        private void SetSurface(bool board)
        {
            if (onBoard == board) return;
            onBoard = board;
            PTUIFactory.Slice(surface, board ? PTUITheme.CorkBoard : PTUITheme.LedgerPage, 2f, board ? CorkFlat : PTUITheme.PaperFlat);
            PTUIFactory.Slice(titleSheet, PTUITheme.PaperSlip, 2.5f, PTUITheme.PaperFlat);
            if (!board) titleSheet.color = Color.clear;
            titleShadow.enabled = board;
            if (titlePin != null) titlePin.SetActive(board);
        }

        // ── State helpers ──────────────────────────────────────────────
        private bool InTown()     => manager.State == PTManager.GameState.Town;
        private bool InCombat()   => manager.State == PTManager.GameState.Combat;
        private bool Confronting() => InTown() && manager.PendingConfrontation != null;
        private bool Raising()     => InTown() && manager.PendingConfrontation == null && manager.PendingRaise != null;
        private bool Eventing()    => InTown() && manager.PendingConfrontation == null && manager.PendingRaise == null && manager.PendingEvent != null;
        private bool Navigable()   => InTown() && !manager.HasPendingDecision;
        private bool ShowView(View v) => Navigable() && view == v;
        private PTSoul MemberAt(int slot) => slot < manager.partyMembers.Count ? manager.partyMembers[slot] : null;
        private PTSoul CandidateAt(int slot) => slot < manager.TavernCandidates.Count ? manager.TavernCandidates[slot] : null;

        // ── Building ───────────────────────────────────────────────────
        private void Build(RectTransform canvas)
        {
            RectTransform panel = PTUIFactory.CreatePanel("Ledger Book", canvas, new Vector2(1f, 1f), new Vector2(-24f, -24f), PanelWidth, 30, 4f);
            panel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(44, 34, 30, 38);   // the binding is on the left, the page edges on the right
            panel.localRotation = Quaternion.Euler(0f, 0f, -0.8f);                         // set down on the desk, not quite square
            surface = panel.GetComponent<Image>();
            PTUIFactory.AddShadow(surface, new Vector2(7f, -7f));
            marginLines = PTUIFactory.AddMarginLines(panel, MarginX);

            BuildSignpost(panel);
            BuildTitle(panel);
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

        private void BuildTitle(RectTransform panel)
        {
            GameObject sheet = new GameObject("Title", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform title = sheet.GetComponent<RectTransform>();
            title.SetParent(panel, false);
            titleSheet = sheet.GetComponent<Image>();
            titleSheet.color = Color.clear;
            titleSheet.raycastTarget = false;
            titleShadow = sheet.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            titleShadow.effectDistance = new Vector2(3f, -4f);
            VerticalLayoutGroup layout = sheet.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 10);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Image pin = PTUIFactory.CreateOrnament("Pin", title, PTUITheme.Pin, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(24f, 24f), 0f);
            titlePin = pin != null ? pin.gameObject : null;

            TextMeshProUGUI header = PTUIFactory.CreateHeading("Header", title, 30f, TextAlignmentOptions.Center, 38f);
            Bind(header.gameObject, () => true, () =>
            {
                switch (manager.State)
                {
                    case PTManager.GameState.Setup:    header.text = "Party Taxes"; break;
                    case PTManager.GameState.Town:     header.text = PlaceName() + " - Day " + manager.daysElapsed + " of " + manager.RunLengthDays; break;
                    case PTManager.GameState.Combat:   header.text = "Field Expenses - Round " + manager.CombatRound; break;
                    case PTManager.GameState.GameOver: header.text = "Game Over"; break;
                    case PTManager.GameState.Victory:  header.text = manager.RunOutcome + "!"; break;
                }
            });

            TextMeshProUGUI status = PTUIFactory.CreateLabel("Status", title, 18f, TextAlignmentOptions.Center, 44f);
            Bind(status.gameObject, () => !manager.IsRunOver && manager.State != PTManager.GameState.Setup, () =>
                status.text = InCombat()
                    ? (manager.ActiveJob != null ? manager.ActiveJob.jobName + " (" + manager.JobPay(manager.ActiveJob) + "g)" : "Ambush!") +
                      "\n" + manager.stance + " stance  ·  Gold " + manager.gold
                    : manager.TributeStatus + "\nPayroll tonight " + manager.PayrollTonight + "g  ·  Average loyalty " +
                      Mathf.RoundToInt(manager.AverageLoyalty));

            PTUIFactory.CreateDivider(title, 16f);
        }

        private string PlaceName()
        {
            if (manager.HasPendingDecision) return "Town";
            switch (view)
            {
                case View.Jobs:   return "Notice Board";
                case View.Tavern: return "Tavern";
                case View.Ledger: return "Counting House";
                default:          return "Town Square";
            }
        }

        /// The town signpost: where you are and where you can go. Hidden in battle and while a decision waits.
        private void BuildSignpost(RectTransform panel)
        {
            RectTransform post = PTUIFactory.CreateSignpost(panel);
            Bind(post.gameObject, Navigable, () => { });
            AddPlank(post, View.Main, () => "Town Square");
            AddPlank(post, View.Jobs, () => manager.hasFought ? "Notice Board" : "Notice Board (" + manager.JobBoard.Count + ")");
            AddPlank(post, View.Tavern, () => "Tavern (" + manager.TavernCandidates.Count + ")");
            AddPlank(post, View.Ledger, () => manager.LastNightMissing > 0 && !manager.hasAudited
                ? "Counting House " + PTUITheme.Tint("!", PTUITheme.RedInk) : "Counting House");
        }

        private void AddPlank(RectTransform post, View destination, Func<string> getLabel)
        {
            TextMeshProUGUI label;
            LayoutElement size;
            Button plank = PTUIFactory.CreatePlank(post, () => SetView(destination), out label, out size);
            refreshers.Add(() =>
            {
                bool here = view == destination;
                label.text = getLabel();
                label.fontStyle = here ? FontStyles.Underline : FontStyles.Normal;
                size.preferredWidth = here ? 244f : 214f;                                  // the board for where you are sticks out further
                plank.interactable = !here;
            });
        }

        private void BuildConfrontation(RectTransform panel)
        {
            RectTransform report = PTUIFactory.CreateCard(panel, PTCardStyle.Report, 0.8f, "Caught");
            Bind(report.gameObject, Confronting, () => { });
            TextMeshProUGUI heading = PTUIFactory.CreateHeading("Heading", report, 22f, TextAlignmentOptions.Left, 28f);
            heading.text = "Incident Report";
            TextMeshProUGUI caught = PTUIFactory.CreateLabel("Caught", report, 20f, TextAlignmentOptions.Left, 52f);
            Bind(caught.gameObject, Confronting, () =>
            {
                PTSoul thief = manager.PendingConfrontation;
                caught.text = "<b>" + thief.Name + "</b> was caught with " + thief.employment.stolenUnrecovered + "g of your gold.";
            });
            AddButton(report, Confronting, () => true,
                () => "Dock their pay  (+" + manager.PendingConfrontation.employment.stolenUnrecovered + "g, loyalty down)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.DockPay), SmallButtonHeight + 4f);
            AddButton(report, Confronting, () => true, () => "Forgive them  (loyalty up, less greedy)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.Forgive), SmallButtonHeight + 4f);
            AddButton(report, Confronting, () => true, () => "Fire them  (no severance)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.Fire), SmallButtonHeight + 4f);
        }

        private void BuildRaisePrompt(RectTransform panel)
        {
            RectTransform petition = PTUIFactory.CreateCard(panel, PTCardStyle.Letter, -0.6f);
            Bind(petition.gameObject, Raising, () => { });
            TextMeshProUGUI heading = PTUIFactory.CreateHeading("Heading", petition, 22f, TextAlignmentOptions.Left, 28f);
            heading.text = "A Petition for a Raise";
            TextMeshProUGUI ask = PTUIFactory.CreateLabel("Raise", petition, 20f, TextAlignmentOptions.Left, 52f);
            Bind(ask.gameObject, Raising, () =>
            {
                PTSoul member = manager.PendingRaise;
                ask.text = "<i>\"I've reached level " + member.level + ". I'd like " + member.employment.requestedWage + "g a day, not " +
                           member.dailywages + "g.\"</i>  - " + member.Name;
            });
            AddButton(petition, Raising, () => true, () => "Accept  (+5 loyalty)", () => manager.ResolveRaise(PTManager.RaiseChoice.Accept), SmallButtonHeight + 4f);
            AddButton(petition, Raising, () => true,
                () => "Counter-offer half  (" + Mathf.RoundToInt(manager.CounterChance(manager.PendingRaise) * 100) + "% chance, else -10 loyalty)",
                () => manager.ResolveRaise(PTManager.RaiseChoice.Counter), SmallButtonHeight + 4f);
            AddButton(petition, Raising, () => true, () => "Refuse  (-15 loyalty)", () => manager.ResolveRaise(PTManager.RaiseChoice.Refuse), SmallButtonHeight + 4f);
        }

        private void BuildEventPrompt(RectTransform panel)
        {
            RectTransform letter = PTUIFactory.CreateCard(panel, PTCardStyle.Letter, 0.5f);
            Bind(letter.gameObject, Eventing, () => { });
            TextMeshProUGUI story = PTUIFactory.CreateLabel("Event", letter, 20f, TextAlignmentOptions.Left, 84f);
            Bind(story.gameObject, Eventing, () =>
                story.text = "<b>" + manager.PendingEvent.title + "</b>\n" + manager.EventText(manager.PendingEvent.text));
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Func<bool> visible = () => Eventing() && index < manager.PendingEvent.choices.Length;
                AddButton(letter, visible, () => manager.CanChooseEventOption(index), () =>
                {
                    PTEventChoice choice = manager.PendingEvent.choices[index];
                    string cost = choice.GoldCost > 0 ? "  -" + choice.GoldCost + "g" : "";
                    string odds = choice.Chance < 1f ? "  (" + Mathf.RoundToInt(choice.Chance * 100) + "% chance)" : "";
                    return manager.EventText(choice.label) + cost + odds;
                }, () => manager.ResolveEvent(index), SmallButtonHeight + 4f);
            }
        }

        private void BuildMainView(RectTransform panel)
        {
            Func<bool> main = () => ShowView(View.Main);
            AddEntry(panel, main, () => true, "",
                () => "Battle stance: <b>" + manager.stance + "</b>", () => "change", manager.CycleStance);
            AddEntry(panel, main, () => !manager.hasHealed && manager.gold >= manager.HealCost, "Q",
                () => manager.hasHealed ? "<s>Heal the party</s>  (the cleric is resting)" : "Heal the party",
                () => manager.hasHealed ? "" : manager.HealCost + "g", manager.TryHeal);
            AddEntry(panel, main, () => !manager.hasBeenBlessed && manager.gold >= manager.BlessCost, "1",
                () => manager.hasBeenBlessed ? "<s>Bless the party</s>  (done today)" : "Bless the party",
                () => manager.hasBeenBlessed ? "" : manager.BlessCost + "g", manager.TryBless);
            AddEntry(panel, () => main() && manager.ResurrectionUnlocked,
                () => manager.FallenCount > 0 && !manager.PartyFull && manager.gold >= manager.CheapestResurrectionCost, "3",
                () => manager.FallenCount == 0 ? "No fallen to raise" : "Raise the fallen  (" + manager.FallenCount + ")",
                () => manager.FallenCount == 0 ? "" : manager.CheapestResurrectionCost + "g+", manager.TryResurrect);
            TextMeshProUGUI hint = PTUIFactory.CreateLabel("Hint", panel, 15f, TextAlignmentOptions.Left, 26f);
            hint.margin = new Vector4(MarginX - 36f, 0f, 0f, 0f);                          // written right of the margin line, like the entries
            Bind(hint.gameObject, main, () => hint.text = PTUITheme.Tint(manager.hasFought
                ? "The day's work is done. Settle up and sleep."
                : "Take a contract from the Notice Board [E], or hire at the Tavern [F].", PTUITheme.FadedInk));
            AddButton(panel, main, () => true, SleepLabel, manager.TrySleep, ButtonHeight, PTButtonStyle.Wax);
        }

        private void BuildJobsView(RectTransform panel)
        {
            Func<bool> jobs = () => ShowView(View.Jobs);
            for (int i = 0; i < 4; i++)                                                    // three contracts, plus the Goblin King on the last day
            {
                int slot = i;
                Func<bool> visible = () => jobs() && slot < manager.JobBoard.Count;
                RectTransform notice = PTUIFactory.CreateCard(panel, PTCardStyle.Notice, slot % 2 == 0 ? -1.4f : 1.1f);
                Bind(notice.gameObject, visible, () => { });
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Job", notice, 18f, TextAlignmentOptions.Left, 48f);
                Bind(info.gameObject, visible, () =>
                {
                    PTJob job = manager.JobBoard[slot];
                    string risk = job.risk == JobRisk.High ? PTUITheme.Tint("High risk", PTUITheme.RedInk) : job.risk == JobRisk.Medium ? "Medium risk" : "Low risk";
                    info.text = "<b>" + job.jobName + "</b>  ·  " + manager.JobPay(job) + "g  ·  " + risk + "\n<size=80%>" + job.description + "</size>";
                });
                AddButton(notice, visible, () => !manager.hasFought,
                    () => manager.JobBoard[slot].kind == JobKind.Boss ? "Face the Goblin King (no retreat!)" : "Take this contract",
                    () => manager.TryTakeJob(slot), SmallButtonHeight);
            }
            AddButton(panel, jobs, () => !manager.hasFought, () => manager.hasFought ? "You've already worked today" : "Take a rest day  (no pay, recover 40% HP)",
                manager.TryRestDay);
        }

        private void BuildCombatView(RectTransform panel)
        {
            AddEntry(panel, InCombat, () => !manager.HazardPaidThisRound && manager.gold >= manager.HazardPayCost, "1",
                () => "Hazard pay  (+25% damage)", () => manager.HazardPayCost + "g", manager.TryHazardPay);
            AddEntry(panel, InCombat, () => !manager.PotionUsedThisRound && manager.gold >= manager.PotionCost && manager.PotionTarget != null, "Q",
                () => manager.PotionTarget != null ? "A potion for " + manager.PotionTarget.Name : "A potion",
                () => manager.PotionCost + "g", manager.TryPotion);
            AddEntry(panel, InCombat, () => !manager.BribedThisRound && manager.BribeTarget != null && manager.gold >= manager.BribeCost, "3",
                () => manager.BribeTarget != null ? "Bribe " + manager.BribeTarget.Name + " to leave" : "The King can't be bought",
                () => manager.BribeTarget != null ? manager.BribeCost + "g" : "", manager.TryBribe);
            AddEntry(panel, InCombat, () => !manager.FocusOrderedThisRound && manager.FocusCandidate != null, "F",
                () => manager.FocusCandidate != null ? "Order focus fire on " + manager.FocusCandidate.Name : "Order focus fire",
                () => "", manager.TryFocusFire);
            AddEntry(panel, InCombat, () => manager.CanRetreat, "R",
                () => manager.CanRetreat ? "Sound the retreat  (no pay)" : "No retreat from the King!",
                () => manager.CanRetreat ? manager.RetreatCost + "g" : "", manager.TryRetreat);
            AddEntry(panel, InCombat, () => true, "Spc",
                () => "Battle speed",
                () => manager.Speed == PTManager.CombatSpeed.Normal ? "1x" : manager.Speed == PTManager.CombatSpeed.Fast ? "2x" : "Skip",
                manager.CycleCombatSpeed);
        }

        private string SleepLabel()
        {
            int due = manager.PayrollTonight + (manager.IsTributeDay ? manager.NextTributeAmount : 0);
            string what = manager.IsTributeDay ? "tribute + payroll " : "payroll ";
            return manager.gold >= due
                ? "Close the books & sleep  (" + what + due + "g)  [Space]"
                : "Close the books & sleep  (" + what + due + "g - you're short!)  [Space]";
        }

        private void BuildTavernView(RectTransform panel)
        {
            Func<bool> tavern = () => ShowView(View.Tavern);
            for (int i = 0; i < manager.TavernSize; i++)
            {
                int slot = i;
                Func<bool> visible = () => tavern() && CandidateAt(slot) != null;
                RectTransform notice = PTUIFactory.CreateCard(panel, PTCardStyle.Notice, slot % 2 == 0 ? 1.2f : -1.3f);
                Bind(notice.gameObject, visible, () => { });
                TextMeshProUGUI info = PTUIFactory.CreateLabel("Candidate", notice, 18f, TextAlignmentOptions.Left, 66f);
                Bind(info.gameObject, visible, () =>
                {
                    PTSoul c = CandidateAt(slot);
                    PTEmployeeRecord job = c.employment;
                    info.text = "<b>Seeking work: " + c.Name + "</b>  L" + c.level + "  ·  " + c.dailywages + "g/day\nHP " + c.maxHP + "  ATK " + c.attack + "  DEF " + c.defense +
                                "  ·  " + (job != null ? job.GreedHint : "") +
                                (job != null && job.traits.Count > 0 ? "\n" + PTUITheme.Tint(job.TraitNames, PTUITheme.GoldInk) : "");
                });
                RectTransform row = PTUIFactory.CreateRow(notice, SmallButtonHeight);
                Bind(row.gameObject, visible, () => { });
                AddContractButton(row, visible, slot, ContractType.DayWage, c => "Wage  " + manager.SigningFee(c, ContractType.DayWage) + "g");
                AddContractButton(row, visible, slot, ContractType.LootShare,
                    c => "Share " + Mathf.RoundToInt(manager.LootSharePercent * 100) + "%  " + manager.SigningFee(c, ContractType.LootShare) + "g");
                AddContractButton(row, visible, slot, ContractType.SigningBonus,
                    c => "Bonus  " + manager.SigningFee(c, ContractType.SigningBonus) + "g, " + manager.ContractWage(c, ContractType.SigningBonus) + "g/day");
            }
            RectTransform terms = PTUIFactory.CreateCard(panel, PTCardStyle.Letter, -0.5f);
            Bind(terms.gameObject, tavern, () => { });
            TextMeshProUGUI hint = PTUIFactory.CreateLabel("ContractHint", terms, 15f, TextAlignmentOptions.Left, 40f);
            Bind(hint.gameObject, tavern, () => hint.text = manager.TavernCandidates.Count == 0
                ? "No one else is looking for work today."
                : "<b>Terms.</b> Wage: pay daily. Share: no wage, a cut of every job. Bonus: big fee now, 60% wage for good.");
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
            TextMeshProUGUI summary = PTUIFactory.CreateLabel("LedgerSummary", panel, 18f, TextAlignmentOptions.Left, 28f);
            Bind(summary.gameObject, ledger, () => summary.text = manager.LastNightMissing > 0
                ? PTUITheme.Tint(manager.LastNightMissing + "g went missing last night.", PTUITheme.RedInk) + " Audit a suspect?"
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
                                               (job.nightsUnpaid > 0 ? "  ·  " + PTUITheme.Tint("unpaid " + job.nightsUnpaid + " night(s)", PTUITheme.RedInk) : "") +
                                               (job.traits.Count > 0 ? "\n" + PTUITheme.Tint(job.TraitNames, PTUITheme.GoldInk) : "") : "");
                });

                RectTransform row = PTUIFactory.CreateRow(panel, SmallButtonHeight);
                Bind(row.gameObject, visible, () => { });
                AddButton(row, visible, () => true,
                    () => MemberAt(slot) != null && MemberAt(slot).employment != null && MemberAt(slot).employment.OnLootShare ? "On share" :
                          MemberAt(slot) != null && MemberAt(slot).employment != null && !MemberAt(slot).employment.payTonight ? "Pay: " + PTUITheme.Tint("NO", PTUITheme.RedInk) : "Pay: yes",
                    () => manager.TogglePay(MemberAt(slot)), SmallButtonHeight);
                AddButton(row, visible, () => !manager.hasAudited,
                    () => manager.hasAudited ? "Audited today" : "Audit",
                    () => { fireConfirm = null; manager.TryAudit(MemberAt(slot)); }, SmallButtonHeight);
                AddButton(row, visible, () => manager.CanBeFired(MemberAt(slot)),
                    () => !manager.CanBeFired(MemberAt(slot)) ? "Can't fire" :
                          fireConfirm != null && fireConfirm == MemberAt(slot) ? PTUITheme.Tint("Sure?", PTUITheme.RedInk) : "Fire  -" + manager.SeveranceFor(MemberAt(slot)) + "g",
                    () => OnFireClicked(MemberAt(slot)), SmallButtonHeight);
            }
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

        /// A ledger line: hotkey in the margin, the entry, and its cost in the debit column.
        private void AddEntry(RectTransform parent, Func<bool> isVisible, Func<bool> isEnabled, string key, Func<string> getLabel, Func<string> getAmount, Action onClick)
        {
            TextMeshProUGUI keyText, label, amount;
            Button entry = PTUIFactory.CreateEntry(parent, EntryHeight, onClick, out keyText, out label, out amount);
            keyText.text = key;
            Bind(entry.gameObject, isVisible, () =>
            {
                bool enabled = isEnabled();
                entry.interactable = enabled;
                label.text = getLabel();
                amount.text = getAmount();
                label.color = enabled ? PTUITheme.Ink : PTUITheme.InkOff;
                amount.color = label.color;
                keyText.color = enabled ? PTUITheme.InkRed : PTUITheme.InkOff;
            });
        }
    }
}
