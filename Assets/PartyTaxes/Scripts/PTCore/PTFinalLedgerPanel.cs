using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// The end-of-run "Final Ledger", a page from your ledger book: how the run ended (stamped across it), the score,
    /// where the money went, and every hire's books, including who was stealing from you all along.
    /// Shown by PTActionBar when the run ends: built at runtime, or re-bound to a baked copy (Party Taxes > Bake UI).
    /// </summary>
    public class PTFinalLedgerPanel : MonoBehaviour
    {
        public const string PrefabName = "PT Final Ledger";
        private const float PanelWidth = 980f;
        private static readonly float[] Columns = { 0.36f, 0.44f, 0.54f, 0.64f, 0.73f };   // the <pos=> columns in BuildTable

        private PTUIBuilder ui;

        public static PTFinalLedgerPanel Show(PTManager manager)
        {
            PTFinalLedgerPanel ledger = PTUIBuilder.FindBaked<PTFinalLedgerPanel>(PrefabName);
            bool baked = ledger != null;
            RectTransform canvas = baked ? ledger.GetComponent<RectTransform>() : PTUIFactory.CreateCanvas(PrefabName, 20, true);
            if (!baked) ledger = canvas.gameObject.AddComponent<PTFinalLedgerPanel>();
            ledger.ui = new PTUIBuilder(baked ? canvas : null);
            ledger.Build(canvas, manager);
            return ledger;
        }

        /// Editor bake: the page with preview text, switched off (it shows itself when a run ends).
        public static GameObject Bake(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas(PrefabName, 20, true);
            PTFinalLedgerPanel ledger = canvas.gameObject.AddComponent<PTFinalLedgerPanel>();
            ledger.ui = new PTUIBuilder(null, true);
            ledger.Build(canvas, manager);
            return canvas.gameObject;
        }

        private void Build(RectTransform canvas, PTManager manager)
        {
            RectTransform panel = ui.Make("Page", () =>
            {
                RectTransform page = PTUIFactory.CreatePanel("Page", canvas, new Vector2(0.5f, 0.5f), Vector2.zero, PanelWidth,
                                                             new RectOffset(66, 54, 44, 50), 8f, PTUITheme.LedgerPage);   // binding on the left, page edges on the right
                PTUIFactory.AddShadow(PTUIFactory.BackgroundOf(page), new Vector2(8f, -8f));
                PTUIFactory.AddMarginLines(page, 48f);
                page.localRotation = Quaternion.Euler(0f, 0f, 0.4f);
                return page;
            });

            ui.Make("Page.Title", () =>
            {
                TextMeshProUGUI title = PTUIFactory.CreateTitle("Page.Title", panel, 60f, 68f);
                title.text = "The Final Ledger";
                return title;
            });
            bool won = manager.State == PTManager.GameState.Victory;
            RectTransform stamp = ui.Make("Page.Stamp", () => PTUIFactory.CreateStamp(panel, "Verdict", PTUITheme.StampRed,
                                                                                    new Vector2(1f, 1f), new Vector2(-136f, -118f), new Vector2(260f, 100f), 9f));
            PTUIFactory.SetStamp(stamp, ui.Baking ? "Pardoned" : manager.RunOutcome, won || ui.Baking ? PTUITheme.StampGreen : PTUITheme.StampRed);
            ui.Make("Page.Divider", () => PTUIFactory.CreateDivider(panel));

            Fill("Page.Subtitle", panel, 24f, TextAlignmentOptions.Center,
                 () => "Day " + manager.daysElapsed + " of " + manager.RunLengthDays + "  ·  Score " + manager.RunScore, "Day 28 of 28  ·  Score 2140");
            Fill("Page.RunInfo", panel, 18f, TextAlignmentOptions.Center,
                 () => PTRunRules.Name(manager.CurrentAccusation) + "  ·  Audit Level " + manager.AuditLevel + "  ·  " +
                       (manager.IsDailyRun ? "Daily run " : "Seed ") + manager.RunSeed, "The Clerk  ·  Audit Level 1  ·  Seed 482913");
            TextMeshProUGUI unlocks = Fill("Page.Unlocks", panel, 22f, TextAlignmentOptions.Center,
                 () => PTUITheme.Tint(string.Join("\n", manager.RunUnlocks), PTUITheme.GoldInk), PTUITheme.Tint("Audit Level 2 unlocked!", PTUITheme.GoldInk));
            if (!ui.Baking) unlocks.gameObject.SetActive(manager.RunUnlocks.Count > 0);
            Fill("Page.Totals", panel, 20f, TextAlignmentOptions.Left, () => BuildTotals(manager),
                 "Tribute paid to the Queen: <b>900g</b><pos=50%>Gold left in your purse: <b>212g</b>\nWages paid: 1210g<pos=50%>Severance paid: 40g\n" +
                 "Stolen from you: 310g<pos=50%>Recovered by audits: 95g\nHeroes hired: 6<pos=50%>Deaths: 3  ·  Strikes: 0");
            TextMeshProUGUI table = Fill("Page.Books", panel, 18f, TextAlignmentOptions.TopLeft, () => BuildTable(manager),
                 "<b><u>Name<pos=36%>Kills<pos=44%>Looted<pos=54%>Wages<pos=64%>Stole<pos=73%>Fate</u></b>\nMira the Bold<pos=36%>14<pos=44%>220g<pos=54%>310g<pos=64%>-<pos=73%>Still with you (Content)");
            PTUIFactory.AddColumnRules(table.rectTransform, Columns);
            Fill("Page.Highlights", panel, 20f, TextAlignmentOptions.Center, () => BuildHighlights(manager),
                 "Biggest thief: <b>Gorrik</b>, 160g behind your back.\nBest earner: <b>Mira the Bold</b>, 220g looted from 14 kills.");

            Button restart = ui.Make("Page.Restart", () =>
            {
                Button button = PTUIFactory.CreateButton(panel, 50f, PTButtonStyle.Wax, 22f);
                PTUIFactory.LabelOf(button).text = "Start a new run  [Esc]";
                return button;
            });
            PTUIFactory.OnClick(restart, manager.RestartRun);
        }

        /// A label whose text comes from the finished run (or a preview while baking).
        private TextMeshProUGUI Fill(string id, RectTransform panel, float size, TextAlignmentOptions alignment, Func<string> getText, string preview)
        {
            TextMeshProUGUI label = ui.Make(id, () => PTUIFactory.CreateLabel(id, panel, size, alignment));
            if (ui.Baking) label.text = preview;
            else label.text = getText();
            return label;
        }

        private static string BuildTotals(PTManager manager)
        {
            int deaths = manager.runLedger.Sum(r => r.timesDied);
            StringBuilder text = new StringBuilder();
            text.Append("Tribute paid to the Queen: <b>").Append(manager.TotalTributePaid).Append("g</b>")
                .Append("<pos=50%>Gold left in your purse: <b>").Append(manager.gold).Append("g</b>\n");
            text.Append("Wages paid: ").Append(manager.TotalWagesPaid).Append("g")
                .Append("<pos=50%>Severance paid: ").Append(manager.TotalSeverancePaid).Append("g\n");
            text.Append("Stolen from you: <color=").Append(PTUITheme.RedInk).Append(">").Append(manager.TotalStolen).Append("g</color>")
                .Append("<pos=50%>Recovered by audits: ").Append(manager.TotalRecovered).Append("g\n");
            text.Append("Heroes hired: ").Append(manager.runLedger.Count)
                .Append("<pos=50%>Deaths: ").Append(deaths).Append("  ·  Strikes: ").Append(manager.TributeStrikes);
            return text.ToString();
        }

        private static string BuildTable(PTManager manager)
        {
            StringBuilder text = new StringBuilder();
            text.Append("<b><u>Name<pos=36%>Kills<pos=44%>Looted<pos=54%>Wages<pos=64%>Stole<pos=73%>Fate</u></b>\n");
            foreach (PTEmployeeRecord record in manager.runLedger)
            {
                string fate = record.IsEmployed ? "Still with you (" + record.Mood + ")" : record.fate;
                text.Append(record.name)
                    .Append("<pos=36%>").Append(record.kills)
                    .Append("<pos=44%>").Append(record.goldLooted).Append("g")
                    .Append("<pos=54%>").Append(record.wagesReceived).Append("g")
                    .Append("<pos=64%>").Append(record.stolenTotal > 0 ? "<color=" + PTUITheme.RedInk + ">" + record.stolenTotal + "g</color>" : "-")
                    .Append("<pos=73%>").Append(fate).Append("\n");
            }
            return text.ToString();
        }

        private static string BuildHighlights(PTManager manager)
        {
            PTEmployeeRecord thief = manager.runLedger.OrderByDescending(r => r.stolenTotal).FirstOrDefault();
            PTEmployeeRecord earner = manager.runLedger.OrderByDescending(r => r.goldLooted).FirstOrDefault();
            string thiefLine = thief != null && thief.stolenTotal > 0
                ? "Biggest thief: <b>" + thief.name + "</b>, " + thief.stolenTotal + "g behind your back."
                : "Nobody stole from you. Remarkable.";
            string earnerLine = earner != null && earner.goldLooted > 0
                ? "Best earner: <b>" + earner.name + "</b>, " + earner.goldLooted + "g looted from " + earner.kills + " kills."
                : "";
            return thiefLine + "\n" + earnerLine;
        }
    }
}
