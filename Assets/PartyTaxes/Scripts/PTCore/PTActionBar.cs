using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// Clickable controls for the whole game, drawn as the accountant's own things. In town, a wooden signpost picks
    /// where you are: the Town Square (your ledger: each action is a line entry, hotkey in the margin, cost in the
    /// debit column), the Notice Board (contracts tacked to cork), the Tavern (people seeking work, on the same board)
    /// and the Counting House (payroll, audits, firing). In battle the ledger lists field expenses. Decisions arrive as
    /// documents: a theft report, a petition for a raise, a letter about the night's events. Keyboard shortcuts keep
    /// working alongside it.
    ///
    /// Built at runtime by PTManager, or re-bound to a baked copy (Party Taxes > Bake UI) so it can be edited in the
    /// scene: every element is made through PTUIBuilder under a unique name, and each view is its own "View.*" group.
    /// PTManager calls Refresh() whenever its UI updates; every element re-reads its state from PTManager.
    /// </summary>
    public class PTActionBar : MonoBehaviour
    {
        public enum View { Main, Jobs, Tavern, Ledger }
        public const string PrefabName = "PT Action Bar";

        private const float PanelWidth   = 500f;
        private const float EntryHeight  = 40f;
        private const float ButtonHeight = 46f;
        private const float OptionHeight = 40f;
        private const float SmallButtonHeight = 38f;
        private const float ContractHeight = 50f;
        private const float MarginX = 84f;                                                 // the ledger's margin line (page padding + hotkey column)
        private static readonly Color CorkFlat = new Color(0.55f, 0.38f, 0.24f, 1f);
        private static readonly string[] HiddenWhenBaked = { "View.Confront", "View.Raise", "View.Event", "View.Jobs", "View.Tavern", "View.Ledger", "View.Combat", "Book.Restart" };

        private PTManager manager;
        private PTUIBuilder ui;
        private View view = View.Main;
        private PTSoul fireConfirm;                                                        // first click on Fire asks for confirmation
        private PTFinalLedgerPanel finalLedger;
        private PTRunSetupPanel runSetup;
        private readonly List<Action> refreshers = new List<Action>();

        private RectTransform book;
        private Image surface;                                                             // the ledger page, or the cork of the notice board
        private GameObject[] marginLines;
        private RectTransform titleSheet;                                                  // the header's own paper, only shown on the board
        private bool? onBoard;

        /// Builds the action bar (or wakes the baked one) and returns it.
        public static PTActionBar Create(PTManager manager)
        {
            PTActionBar bar = PTUIBuilder.FindBaked<PTActionBar>(PrefabName);
            bool baked = bar != null;
            RectTransform canvas = baked ? bar.GetComponent<RectTransform>() : PTUIFactory.CreateCanvas(PrefabName, 10);
            if (!baked) bar = canvas.gameObject.AddComponent<PTActionBar>();
            bar.manager = manager;
            bar.ui = new PTUIBuilder(baked ? canvas : null);
            bar.Build(canvas);
            bar.Refresh();
            return bar;
        }

        /// Editor bake: the whole action bar with preview text, Town Square showing and the other views switched off.
        public static GameObject Bake(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas(PrefabName, 10);
            PTActionBar bar = canvas.gameObject.AddComponent<PTActionBar>();
            bar.manager = manager;
            bar.ui = new PTUIBuilder(null, true);
            bar.Build(canvas);
            foreach (string group in HiddenWhenBaked)
            {
                Transform found = bar.book.Find(group);
                if (found != null) found.gameObject.SetActive(false);
            }
            bar.SetSurface(false);
            return canvas.gameObject;
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
            Image sheet = PTUIFactory.BackgroundOf(titleSheet);
            if (sheet != null) sheet.enabled = board;
            Transform pin = titleSheet.Find("Pin");
            if (pin != null) pin.gameObject.SetActive(board);
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
            book = ui.Make("Book", () =>
            {
                RectTransform panel = PTUIFactory.CreatePanel("Book", canvas, new Vector2(1f, 1f), new Vector2(-24f, -24f), PanelWidth,
                                                              new RectOffset(46, 36, 30, 40), 6f, PTUITheme.LedgerPage);   // binding on the left, page edges on the right
                panel.localRotation = Quaternion.Euler(0f, 0f, -0.5f);                     // set down on the desk, not quite square
                PTUIFactory.AddShadow(PTUIFactory.BackgroundOf(panel), new Vector2(7f, -7f));
                return panel;
            });
            surface = PTUIFactory.BackgroundOf(book);
            marginLines = PTUIFactory.AddMarginLines(book, MarginX);

            BuildSignpost();
            BuildTitle();
            BuildConfrontation(Group("View.Confront", () => PTUIFactory.CreateCard(book, PTCardStyle.Report, 0.6f, "Caught")), Confronting);
            BuildRaisePrompt(Group("View.Raise", () => PTUIFactory.CreateCard(book, PTCardStyle.Letter, -0.5f)), Raising);
            BuildEventPrompt(Group("View.Event", () => PTUIFactory.CreateCard(book, PTCardStyle.Letter, 0.4f)), Eventing);
            BuildMainView(Group("View.Main", () => PTUIFactory.CreateGroup("View.Main", book, 6f)), () => ShowView(View.Main));
            BuildJobsView(Group("View.Jobs", () => PTUIFactory.CreateGroup("View.Jobs", book, 10f)), () => ShowView(View.Jobs));
            BuildTavernView(Group("View.Tavern", () => PTUIFactory.CreateGroup("View.Tavern", book, 10f)), () => ShowView(View.Tavern));
            BuildLedgerView(Group("View.Ledger", () => PTUIFactory.CreateGroup("View.Ledger", book, 6f)), () => ShowView(View.Ledger));
            BuildCombatView(Group("View.Combat", () => PTUIFactory.CreateGroup("View.Combat", book, 6f)), InCombat);

            AddButton("Book.Restart", book, () => manager.IsRunOver, () => true, () => "Start a new run  [Esc]", manager.RestartRun,
                      ButtonHeight, PTButtonStyle.Wax);
        }

        private RectTransform Group(string id, Func<RectTransform> create) => ui.Make(id, create);

        private void BuildTitle()
        {
            titleSheet = ui.Make("Book.Title", () =>
            {
                RectTransform sheet = PTUIFactory.CreateGroup("Book.Title", book, 2f);
                sheet.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 14, 8);
                Image paper = PTUIFactory.SetBackground(sheet, PTUITheme.PaperSlip, 2.5f, PTUITheme.PaperFlat);
                PTUIFactory.AddShadow(paper, new Vector2(3f, -4f));
                paper.enabled = false;
                Image pin = PTUIFactory.CreateOrnament("Pin", sheet, PTUITheme.Pin, new Vector2(0.5f, 1f), new Vector2(0f, -7f), new Vector2(22f, 22f), 0f);
                if (pin != null) pin.gameObject.SetActive(false);
                return sheet;
            });

            TextMeshProUGUI header = ui.Make("Book.Header", () => PTUIFactory.CreateHeading("Book.Header", titleSheet, 28f, TextAlignmentOptions.Center, 38f));
            Func<string> headerText = () =>
            {
                switch (manager.State)
                {
                    case PTManager.GameState.Setup:    return "Party Taxes";
                    case PTManager.GameState.Town:     return PlaceName() + " - Day " + manager.daysElapsed + " of " + manager.RunLengthDays;
                    case PTManager.GameState.Combat:   return "Field Expenses - Round " + manager.CombatRound;
                    case PTManager.GameState.GameOver: return "Game Over";
                    default:                           return manager.RunOutcome + "!";
                }
            };
            ui.Preview(header, () => "Town Square - Day 1 of 28", "Header");
            Bind(header.gameObject, () => true, () => header.text = headerText());

            TextMeshProUGUI status = ui.Make("Book.Status", () => PTUIFactory.CreateLabel("Book.Status", titleSheet, 19f));
            ui.Preview(status, () => "Tribute: 225g due in 6 days\nPayroll tonight 28g  ·  Average loyalty 60", "Status");
            Bind(status.gameObject, () => !manager.IsRunOver && manager.State != PTManager.GameState.Setup, () =>
                status.text = InCombat()
                    ? (manager.ActiveJob != null ? manager.ActiveJob.jobName + " (" + manager.JobPay(manager.ActiveJob) + "g)" : "Ambush!") +
                      "\n" + manager.stance + " stance  ·  Gold " + manager.gold
                    : manager.TributeStatus + "\nPayroll tonight " + manager.PayrollTonight + "g  ·  Average loyalty " +
                      Mathf.RoundToInt(manager.AverageLoyalty));

            ui.Make("Book.Divider", () => PTUIFactory.CreateDivider(titleSheet, 14f));
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
        private void BuildSignpost()
        {
            RectTransform post = ui.Make("Book.Signpost", () => PTUIFactory.CreateSignpost(book, new Vector2(4f, -34f)));
            Bind(post.gameObject, Navigable, () => { });
            AddPlank(post, "Sign.Square", View.Main, () => "Town Square");
            AddPlank(post, "Sign.Board", View.Jobs, () => manager.hasFought ? "Notice Board" : "Notice Board (" + manager.JobBoard.Count + ")");
            AddPlank(post, "Sign.Tavern", View.Tavern, () => "Tavern (" + manager.TavernCandidates.Count + ")");
            AddPlank(post, "Sign.Counting", View.Ledger, () => manager.LastNightMissing > 0 && !manager.hasAudited
                ? "Counting House " + PTUITheme.Tint("!", PTUITheme.RedInk) : "Counting House");
        }

        private void AddPlank(RectTransform post, string id, View destination, Func<string> getLabel)
        {
            Button plank = ui.Make(id, () => PTUIFactory.CreatePlank(post, 204f, 42f));
            TextMeshProUGUI label = PTUIFactory.LabelOf(plank);
            LayoutElement size = plank.GetComponent<LayoutElement>();
            float width = size.preferredWidth;
            PTUIFactory.OnClick(plank, () => SetView(destination));
            ui.Preview(label, getLabel, id);
            refreshers.Add(() =>
            {
                bool here = view == destination;
                label.text = getLabel();
                label.fontStyle = here ? FontStyles.Underline : FontStyles.Normal;
                size.preferredWidth = here ? width + 18f : width;                          // the board for where you are sticks out further
                plank.interactable = !here;
            });
        }

        private void BuildConfrontation(RectTransform report, Func<bool> visible)
        {
            Bind(report.gameObject, visible, () => { });
            ui.Make("Confront.Heading", () =>
            {
                TextMeshProUGUI heading = PTUIFactory.CreateHeading("Confront.Heading", report, PTUITheme.SizeSubhead, TextAlignmentOptions.Left);
                heading.text = "Incident Report";
                return heading;
            });
            TextMeshProUGUI caught = ui.Make("Confront.Text", () => PTUIFactory.CreateLabel("Confront.Text", report, 20f, TextAlignmentOptions.Left));
            Func<string> caughtText = () =>
            {
                PTSoul thief = manager.PendingConfrontation;
                return "<b>" + thief.Name + "</b> was caught with " + thief.employment.stolenUnrecovered + "g of your gold.";
            };
            ui.Preview(caught, caughtText, "<b>Name</b> was caught with 34g of your gold.");
            Bind(caught.gameObject, visible, () => caught.text = caughtText());
            AddButton("Confront.Dock", report, visible, () => true,
                () => "Dock their pay  (+" + manager.PendingConfrontation.employment.stolenUnrecovered + "g, loyalty down)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.DockPay), OptionHeight);
            AddButton("Confront.Forgive", report, visible, () => true, () => "Forgive them  (loyalty up, less greedy)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.Forgive), OptionHeight);
            AddButton("Confront.Fire", report, visible, () => true, () => "Fire them  (no severance)",
                () => manager.ResolveConfrontation(PTManager.Confrontation.Fire), OptionHeight);
        }

        private void BuildRaisePrompt(RectTransform petition, Func<bool> visible)
        {
            Bind(petition.gameObject, visible, () => { });
            ui.Make("Raise.Heading", () =>
            {
                TextMeshProUGUI heading = PTUIFactory.CreateHeading("Raise.Heading", petition, PTUITheme.SizeSubhead, TextAlignmentOptions.Left);
                heading.text = "A Petition for a Raise";
                return heading;
            });
            TextMeshProUGUI ask = ui.Make("Raise.Text", () => PTUIFactory.CreateLabel("Raise.Text", petition, 20f, TextAlignmentOptions.Left));
            Func<string> askText = () =>
            {
                PTSoul member = manager.PendingRaise;
                return "<i>\"I've reached level " + member.level + ". I'd like " + member.employment.requestedWage + "g a day, not " +
                       member.dailywages + "g.\"</i>  - " + member.Name;
            };
            ui.Preview(ask, askText, "<i>\"I've reached level 3. I'd like 18g a day, not 13g.\"</i>  - Name");
            Bind(ask.gameObject, visible, () => ask.text = askText());
            AddButton("Raise.Accept", petition, visible, () => true, () => "Accept  (+5 loyalty)",
                () => manager.ResolveRaise(PTManager.RaiseChoice.Accept), OptionHeight);
            AddButton("Raise.Counter", petition, visible, () => true,
                () => "Counter-offer half  (" + Mathf.RoundToInt(manager.CounterChance(manager.PendingRaise) * 100) + "% chance, else -10 loyalty)",
                () => manager.ResolveRaise(PTManager.RaiseChoice.Counter), OptionHeight);
            AddButton("Raise.Refuse", petition, visible, () => true, () => "Refuse  (-15 loyalty)",
                () => manager.ResolveRaise(PTManager.RaiseChoice.Refuse), OptionHeight);
        }

        private void BuildEventPrompt(RectTransform letter, Func<bool> visible)
        {
            Bind(letter.gameObject, visible, () => { });
            TextMeshProUGUI story = ui.Make("Event.Text", () => PTUIFactory.CreateLabel("Event.Text", letter, 20f, TextAlignmentOptions.Left));
            Func<string> storyText = () => "<b>" + manager.PendingEvent.title + "</b>\n" + manager.EventText(manager.PendingEvent.text);
            ui.Preview(story, storyText, "<b>A Night Event</b>\nWhat happened in the night, and what it asks of you.");
            Bind(story.gameObject, visible, () => story.text = storyText());
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Func<bool> choiceVisible = () => visible() && index < manager.PendingEvent.choices.Length;
                AddButton("Event.Choice" + i, letter, choiceVisible, () => manager.CanChooseEventOption(index), () =>
                {
                    PTEventChoice choice = manager.PendingEvent.choices[index];
                    string cost = choice.GoldCost > 0 ? "  -" + choice.GoldCost + "g" : "";
                    string odds = choice.Chance < 1f ? "  (" + Mathf.RoundToInt(choice.Chance * 100) + "% chance)" : "";
                    return manager.EventText(choice.label) + cost + odds;
                }, () => manager.ResolveEvent(index), OptionHeight);
            }
        }

        private void BuildMainView(RectTransform group, Func<bool> main)
        {
            Bind(group.gameObject, main, () => { });
            AddEntry("Main.Stance", group, main, () => true, "",
                () => "Battle stance: <b>" + manager.stance + "</b>", () => "change", manager.CycleStance);
            AddEntry("Main.Heal", group, main, () => !manager.hasHealed && manager.gold >= manager.HealCost, "Q",
                () => manager.hasHealed ? "<s>Heal the party</s>  (the cleric is resting)" : "Heal the party",
                () => manager.hasHealed ? "" : manager.HealCost + "g", manager.TryHeal);
            AddEntry("Main.Bless", group, main, () => !manager.hasBeenBlessed && manager.gold >= manager.BlessCost, "1",
                () => manager.hasBeenBlessed ? "<s>Bless the party</s>  (done today)" : "Bless the party",
                () => manager.hasBeenBlessed ? "" : manager.BlessCost + "g", manager.TryBless);
            AddEntry("Main.Resurrect", group, () => main() && manager.ResurrectionUnlocked,
                () => manager.FallenCount > 0 && !manager.PartyFull && manager.gold >= manager.CheapestResurrectionCost, "3",
                () => manager.FallenCount == 0 ? "No fallen to raise" : "Raise the fallen  (" + manager.FallenCount + ")",
                () => manager.FallenCount == 0 ? "" : manager.CheapestResurrectionCost + "g+", manager.TryResurrect);

            TextMeshProUGUI hint = ui.Make("Main.Hint", () =>
            {
                TextMeshProUGUI label = PTUIFactory.CreateLabel("Main.Hint", group, PTUITheme.SizeSmall, TextAlignmentOptions.Left);
                label.margin = new Vector4(MarginX - 34f, 4f, 0f, 2f);                     // written right of the margin line, like the entries
                label.color = new Color(PTUITheme.Ink.r, PTUITheme.Ink.g, PTUITheme.Ink.b, 0.7f);
                return label;
            });
            Func<string> hintText = () => manager.hasFought
                ? "The day's work is done. Settle up and sleep."
                : "Take a contract from the Notice Board [E], or hire at the Tavern [F].";
            ui.Preview(hint, hintText, "Take a contract from the Notice Board [E], or hire at the Tavern [F].");
            Bind(hint.gameObject, main, () => hint.text = hintText());
            AddButton("Main.Sleep", group, main, () => true, SleepLabel, manager.TrySleep, ButtonHeight, PTButtonStyle.Wax);
        }

        private void BuildJobsView(RectTransform group, Func<bool> jobs)
        {
            Bind(group.gameObject, jobs, () => { });
            for (int i = 0; i < 4; i++)                                                    // three contracts, plus the Goblin King on the last day
            {
                int slot = i;
                string id = "Jobs.Notice" + i;
                Func<bool> visible = () => jobs() && slot < manager.JobBoard.Count;
                RectTransform notice = ui.Make(id, () => PTUIFactory.CreateCard(group, PTCardStyle.Notice, slot % 2 == 0 ? -1.2f : 1f));
                Bind(notice.gameObject, visible, () => { });
                TextMeshProUGUI info = ui.Make(id + ".Info", () => PTUIFactory.CreateLabel(id + ".Info", notice, 19f, TextAlignmentOptions.Left));
                Func<string> infoText = () =>
                {
                    PTJob job = manager.JobBoard[slot];
                    string risk = job.risk == JobRisk.High ? PTUITheme.Tint("High risk", PTUITheme.RedInk) : job.risk == JobRisk.Medium ? "Medium risk" : "Low risk";
                    return "<b>" + job.jobName + "</b>  ·  " + manager.JobPay(job) + "g  ·  " + risk + "\n<size=88%>" + job.description + "</size>";
                };
                ui.Preview(info, infoText, "<b>Clear a goblin nest</b>  ·  30g  ·  Medium risk\n<size=88%>The bread and butter of frontier work.</size>");
                Bind(info.gameObject, visible, () => info.text = infoText());
                AddButton(id + ".Take", notice, visible, () => !manager.hasFought,
                    () => manager.JobBoard[slot].kind == JobKind.Boss ? "Face the Goblin King (no retreat!)" : "Take this contract",
                    () => manager.TryTakeJob(slot), SmallButtonHeight);
            }
            AddButton("Jobs.Rest", group, jobs, () => !manager.hasFought,
                () => manager.hasFought ? "You've already worked today" : "Take a rest day  (no pay, recover 40% HP)", manager.TryRestDay, SmallButtonHeight);
        }

        private void BuildCombatView(RectTransform group, Func<bool> combat)
        {
            Bind(group.gameObject, combat, () => { });
            AddEntry("Combat.Hazard", group, combat, () => !manager.HazardPaidThisRound && manager.gold >= manager.HazardPayCost, "1",
                () => "Hazard pay  (+25% damage)", () => manager.HazardPayCost + "g", manager.TryHazardPay);
            AddEntry("Combat.Potion", group, combat, () => !manager.PotionUsedThisRound && manager.gold >= manager.PotionCost && manager.PotionTarget != null, "Q",
                () => manager.PotionTarget != null ? "A potion for " + manager.PotionTarget.Name : "A potion",
                () => manager.PotionCost + "g", manager.TryPotion);
            AddEntry("Combat.Bribe", group, combat, () => !manager.BribedThisRound && manager.BribeTarget != null && manager.gold >= manager.BribeCost, "3",
                () => manager.BribeTarget != null ? "Bribe " + manager.BribeTarget.Name + " to leave" : "The King can't be bought",
                () => manager.BribeTarget != null ? manager.BribeCost + "g" : "", manager.TryBribe);
            AddEntry("Combat.Focus", group, combat, () => !manager.FocusOrderedThisRound && manager.FocusCandidate != null, "F",
                () => manager.FocusCandidate != null ? "Order focus fire on " + manager.FocusCandidate.Name : "Order focus fire",
                () => "", manager.TryFocusFire);
            AddEntry("Combat.Retreat", group, combat, () => manager.CanRetreat, "R",
                () => manager.CanRetreat ? "Sound the retreat  (no pay)" : "No retreat from the King!",
                () => manager.CanRetreat ? manager.RetreatCost + "g" : "", manager.TryRetreat);
            AddEntry("Combat.Speed", group, combat, () => true, "Spc",
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

        private void BuildTavernView(RectTransform group, Func<bool> tavern)
        {
            Bind(group.gameObject, tavern, () => { });
            for (int i = 0; i < manager.TavernSize; i++)
            {
                int slot = i;
                string id = "Tavern.Notice" + i;
                Func<bool> visible = () => tavern() && CandidateAt(slot) != null;
                RectTransform notice = ui.Make(id, () => PTUIFactory.CreateCard(group, PTCardStyle.Notice, slot % 2 == 0 ? 1f : -1.1f));
                Bind(notice.gameObject, visible, () => { });
                TextMeshProUGUI info = ui.Make(id + ".Info", () => PTUIFactory.CreateLabel(id + ".Info", notice, 19f, TextAlignmentOptions.Left));
                Func<string> infoText = () =>
                {
                    PTSoul c = CandidateAt(slot);
                    PTEmployeeRecord job = c.employment;
                    return "<b>Seeking work: " + c.Name + "</b>  L" + c.level + "  ·  " + c.dailywages + "g/day\nHP " + c.maxHP + "  ATK " + c.attack + "  DEF " + c.defense +
                           "  ·  " + (job != null ? job.GreedHint : "") +
                           (job != null && job.traits.Count > 0 ? "\n" + PTUITheme.Tint(job.TraitNames, PTUITheme.GoldInk) : "");
                };
                ui.Preview(info, infoText, "<b>Seeking work: Name</b>  L1  ·  15g/day\nHP 80  ATK 6  DEF 8  ·  honest face\n" + PTUITheme.Tint("Loyal", PTUITheme.GoldInk));
                Bind(info.gameObject, visible, () => info.text = infoText());
                RectTransform row = ui.Make(id + ".Terms", () => PTUIFactory.CreateRow(notice, ContractHeight));
                Bind(row.gameObject, visible, () => { });
                AddContractButton(id + ".Wage", row, visible, slot, ContractType.DayWage,
                    c => "Day wage\n<b>" + manager.SigningFee(c, ContractType.DayWage) + "g</b> to sign");
                AddContractButton(id + ".Share", row, visible, slot, ContractType.LootShare,
                    c => Mathf.RoundToInt(manager.LootSharePercent * 100) + "% share\n<b>" + manager.SigningFee(c, ContractType.LootShare) + "g</b> to sign");
                AddContractButton(id + ".Bonus", row, visible, slot, ContractType.SigningBonus,
                    c => "Bonus <b>" + manager.SigningFee(c, ContractType.SigningBonus) + "g</b>\nthen " + manager.ContractWage(c, ContractType.SigningBonus) + "g/day");
            }
            RectTransform terms = ui.Make("Tavern.Terms", () => PTUIFactory.CreateCard(group, PTCardStyle.Letter, -0.4f));
            Bind(terms.gameObject, tavern, () => { });
            TextMeshProUGUI hint = ui.Make("Tavern.TermsText", () =>
            {
                TextMeshProUGUI label = PTUIFactory.CreateLabel("Tavern.TermsText", terms, PTUITheme.SizeSmall, TextAlignmentOptions.Left);
                label.margin = new Vector4(0f, 0f, 40f, 0f);                               // clear of the wax seal
                return label;
            });
            Func<string> termsText = () => manager.TavernCandidates.Count == 0
                ? "No one else is looking for work today."
                : "<b>Terms.</b> Day wage: paid nightly. Share: no wage, a cut of every job. Bonus: a big fee now, then 60% wage for good.";
            ui.Preview(hint, termsText, "<b>Terms.</b> Day wage: paid nightly. Share: no wage, a cut of every job. Bonus: a big fee now, then 60% wage for good.");
            Bind(hint.gameObject, tavern, () => hint.text = termsText());
        }

        private void AddContractButton(string id, RectTransform row, Func<bool> visible, int slot, ContractType contract, Func<PTSoul, string> label)
        {
            AddButton(id, row, visible,
                () => !manager.PartyFull && CandidateAt(slot) != null && manager.gold >= manager.SigningFee(CandidateAt(slot), contract),
                () => CandidateAt(slot) == null ? "" : manager.PartyFull ? "Party full" : label(CandidateAt(slot)),
                () => manager.TryHire(slot, contract), ContractHeight, PTButtonStyle.Paper, 17f);
        }

        private void BuildLedgerView(RectTransform group, Func<bool> ledger)
        {
            Bind(group.gameObject, ledger, () => { });
            TextMeshProUGUI summary = ui.Make("Ledger.Summary", () => PTUIFactory.CreateLabel("Ledger.Summary", group, 20f, TextAlignmentOptions.Left));
            Func<string> summaryText = () => manager.LastNightMissing > 0
                ? PTUITheme.Tint(manager.LastNightMissing + "g went missing last night.", PTUITheme.RedInk) + " Audit a suspect?"
                : "The books balanced last night.";
            ui.Preview(summary, summaryText, "The books balanced last night.");
            Bind(summary.gameObject, ledger, () => summary.text = summaryText());

            for (int i = 0; i < manager.maxPartyCount; i++)
            {
                int slot = i;
                string id = "Ledger.Member" + i;
                Func<bool> visible = () => ledger() && MemberAt(slot) != null;
                TextMeshProUGUI info = ui.Make(id + ".Info", () =>
                {
                    TextMeshProUGUI label = PTUIFactory.CreateLabel(id + ".Info", group, 19f, TextAlignmentOptions.Left);
                    label.margin = new Vector4(0f, 6f, 0f, 0f);
                    return label;
                });
                Func<string> infoText = () =>
                {
                    PTSoul m = MemberAt(slot);
                    PTEmployeeRecord job = m.employment;
                    string pay = job != null && job.OnLootShare ? Mathf.RoundToInt(manager.LootSharePercent * 100) + "% share" : m.dailywages + "g/day";
                    return "<b>" + m.Name + "</b>  L" + m.level + "  ·  " + pay + "\n" +
                           (job != null ? job.Mood + " (" + job.loyalty + ")  ·  " + job.GreedHint +
                                          (job.nightsUnpaid > 0 ? "  ·  " + PTUITheme.Tint("unpaid " + job.nightsUnpaid + " night(s)", PTUITheme.RedInk) : "") +
                                          (job.traits.Count > 0 ? "\n" + PTUITheme.Tint(job.TraitNames, PTUITheme.GoldInk) : "") : "");
                };
                ui.Preview(info, infoText, "<b>Name</b>  L1  ·  13g/day\nContent (60)  ·  hard to read");
                Bind(info.gameObject, visible, () => info.text = infoText());

                RectTransform row = ui.Make(id + ".Row", () => PTUIFactory.CreateRow(group, SmallButtonHeight));
                Bind(row.gameObject, visible, () => { });
                AddButton(id + ".Pay", row, visible, () => true,
                    () => MemberAt(slot) != null && MemberAt(slot).employment != null && MemberAt(slot).employment.OnLootShare ? "On share" :
                          MemberAt(slot) != null && MemberAt(slot).employment != null && !MemberAt(slot).employment.payTonight ? "Pay: " + PTUITheme.Tint("NO", PTUITheme.RedInk) : "Pay: yes",
                    () => manager.TogglePay(MemberAt(slot)), SmallButtonHeight);
                AddButton(id + ".Audit", row, visible, () => !manager.hasAudited,
                    () => manager.hasAudited ? "Audited today" : "Audit",
                    () => { fireConfirm = null; manager.TryAudit(MemberAt(slot)); }, SmallButtonHeight);
                AddButton(id + ".Fire", row, visible, () => manager.CanBeFired(MemberAt(slot)),
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
                if (element.activeSelf != visible) element.SetActive(visible);
                if (visible) update();
            });
        }

        private void AddButton(string id, RectTransform parent, Func<bool> isVisible, Func<bool> isEnabled, Func<string> getLabel, Action onClick,
                               float height, PTButtonStyle style = PTButtonStyle.Paper, float textSize = PTUITheme.SizeButton)
        {
            Button button = ui.Make(id, () => PTUIFactory.CreateButton(parent, height, style, textSize));
            TextMeshProUGUI label = PTUIFactory.LabelOf(button);
            Color on = label.color;                                                        // whatever colour it was given (in code or in the editor)
            Color off = new Color(on.r, on.g, on.b, on.a * 0.42f);
            PTUIFactory.OnClick(button, onClick);
            ui.Preview(label, getLabel, id);
            Bind(button.gameObject, isVisible, () =>
            {
                bool enabled = isEnabled();
                button.interactable = enabled;
                label.text = getLabel();
                label.color = enabled ? on : off;
            });
        }

        /// A ledger line: hotkey in the margin, the entry, and its cost in the debit column.
        private void AddEntry(string id, RectTransform parent, Func<bool> isVisible, Func<bool> isEnabled, string key, Func<string> getLabel, Func<string> getAmount, Action onClick)
        {
            Button entry = ui.Make(id, () =>
            {
                Button made = PTUIFactory.CreateEntry(parent, EntryHeight);
                PTUIFactory.LabelOf(made, "Key").text = key;
                return made;
            });
            TextMeshProUGUI keyText = PTUIFactory.LabelOf(entry, "Key");
            TextMeshProUGUI label = PTUIFactory.LabelOf(entry, "Label");
            TextMeshProUGUI amount = PTUIFactory.LabelOf(entry, "Amount");
            Color ink = label.color, keyInk = keyText.color;
            Color faded = new Color(ink.r, ink.g, ink.b, ink.a * 0.4f);
            PTUIFactory.OnClick(entry, onClick);
            ui.Preview(label, getLabel, id);
            ui.Preview(amount, getAmount, "");
            Bind(entry.gameObject, isVisible, () =>
            {
                bool enabled = isEnabled();
                entry.interactable = enabled;
                label.text = getLabel();
                amount.text = getAmount();
                label.color = enabled ? ink : faded;
                amount.color = label.color;
                keyText.color = enabled ? keyInk : faded;
            });
        }
    }
}
