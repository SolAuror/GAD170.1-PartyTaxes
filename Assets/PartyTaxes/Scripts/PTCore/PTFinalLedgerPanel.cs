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
    /// Built at runtime by PTActionBar when the run ends.
    /// </summary>
    public class PTFinalLedgerPanel : MonoBehaviour
    {
        private const float PanelWidth = 980f;

        public static PTFinalLedgerPanel Show(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas("PT Final Ledger", 20, true);
            PTFinalLedgerPanel ledger = canvas.gameObject.AddComponent<PTFinalLedgerPanel>();
            ledger.Build(canvas, manager);
            return ledger;
        }

        private void Build(RectTransform canvas, PTManager manager)
        {
            RectTransform panel = PTUIFactory.CreatePanel("Ledger Page", canvas, new Vector2(0.5f, 0.5f), Vector2.zero, PanelWidth, 48, 10f);
            panel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(64, 52, 44, 52);   // binding on the left, page edges on the right
            PTUIFactory.Slice(panel.GetComponent<Image>(), PTUITheme.LedgerPage, 2f, PTUITheme.PaperFlat);
            PTUIFactory.AddShadow(panel.GetComponent<Image>(), new Vector2(8f, -8f));
            PTUIFactory.AddMarginLines(panel, 48f);
            panel.localRotation = Quaternion.Euler(0f, 0f, 0.6f);

            TextMeshProUGUI title = PTUIFactory.CreateTitle("Title", panel, 60f, 70f);
            title.text = "The Final Ledger";
            PTUIFactory.CreateStamp(panel, manager.RunOutcome, manager.State == PTManager.GameState.Victory ? PTUITheme.StampGreen : PTUITheme.StampRed,
                                    new Vector2(1f, 1f), new Vector2(-136f, -126f), new Vector2(260f, 104f), 9f);
            PTUIFactory.CreateDivider(panel);

            TextMeshProUGUI subtitle = PTUIFactory.CreateLabel("Subtitle", panel, 24f, TextAlignmentOptions.Center, 34f);
            subtitle.text = "Day " + manager.daysElapsed + " of " + manager.RunLengthDays + "  ·  Score " + manager.RunScore;

            TextMeshProUGUI runInfo = PTUIFactory.CreateLabel("RunInfo", panel, 18f, TextAlignmentOptions.Center, 28f);
            runInfo.text = PTRunRules.Name(manager.CurrentAccusation) + "  ·  Audit Level " + manager.AuditLevel + "  ·  " +
                           (manager.IsDailyRun ? "Daily run " : "Seed ") + manager.RunSeed;

            if (manager.RunUnlocks.Count > 0)
            {
                TextMeshProUGUI unlocks = PTUIFactory.CreateLabel("Unlocks", panel, 22f, TextAlignmentOptions.Center, 30f * manager.RunUnlocks.Count);
                unlocks.text = "<color=" + PTUITheme.GoldInk + ">" + string.Join("\n", manager.RunUnlocks) + "</color>";
            }

            TextMeshProUGUI totals = PTUIFactory.CreateLabel("Totals", panel, 20f, TextAlignmentOptions.Left, 110f);
            totals.text = BuildTotals(manager);

            TextMeshProUGUI table = PTUIFactory.CreateLabel("Books", panel, 18f, TextAlignmentOptions.TopLeft, 40f + 26f * manager.runLedger.Count);
            table.text = BuildTable(manager);
            PTUIFactory.AddColumnRules(table.rectTransform, new[] { 0.36f, 0.44f, 0.54f, 0.64f, 0.73f });   // the <pos=> columns in BuildTable

            TextMeshProUGUI highlights = PTUIFactory.CreateLabel("Highlights", panel, 20f, TextAlignmentOptions.Center, 56f);
            highlights.text = BuildHighlights(manager);

            TextMeshProUGUI restartLabel;
            PTUIFactory.CreateButton(panel, 50f, manager.RestartRun, out restartLabel, PTButtonStyle.Wax);
            restartLabel.text = "Start a new run  [Esc]";
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
