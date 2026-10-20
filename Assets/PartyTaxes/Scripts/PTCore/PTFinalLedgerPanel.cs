using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// The end-of-run "Final Ledger": how the run ended, the score, where the money went,
    /// and every hire's books, including who was stealing from you all along.
    /// Built at runtime by PTActionBar when the run ends.
    /// </summary>
    public class PTFinalLedgerPanel : MonoBehaviour
    {
        private const float PanelWidth = 980f;

        public static PTFinalLedgerPanel Show(PTManager manager)
        {
            RectTransform canvas = PTUIFactory.CreateCanvas("PT Final Ledger", 20);
            PTFinalLedgerPanel ledger = canvas.gameObject.AddComponent<PTFinalLedgerPanel>();
            ledger.Build(canvas, manager);
            return ledger;
        }

        private void Build(RectTransform canvas, PTManager manager)
        {
            RectTransform panel = PTUIFactory.CreatePanel("Panel", canvas, new Vector2(0.5f, 0.5f), Vector2.zero, PanelWidth, 24, 10f);

            TextMeshProUGUI title = PTUIFactory.CreateLabel("Title", panel, 46f, TextAlignmentOptions.Center, 60f);
            title.fontStyle = FontStyles.Bold;
            title.text = "The Final Ledger: " + manager.RunOutcome;

            TextMeshProUGUI subtitle = PTUIFactory.CreateLabel("Subtitle", panel, 24f, TextAlignmentOptions.Center, 34f);
            subtitle.text = "Day " + manager.daysElapsed + " of " + manager.RunLengthDays + "  ·  Score " + manager.RunScore;

            TextMeshProUGUI runInfo = PTUIFactory.CreateLabel("RunInfo", panel, 18f, TextAlignmentOptions.Center, 28f);
            runInfo.text = PTRunRules.Name(manager.CurrentAccusation) + "  ·  Audit Level " + manager.AuditLevel + "  ·  " +
                           (manager.IsDailyRun ? "Daily run " : "Seed ") + manager.RunSeed;

            if (manager.RunUnlocks.Count > 0)
            {
                TextMeshProUGUI unlocks = PTUIFactory.CreateLabel("Unlocks", panel, 22f, TextAlignmentOptions.Center, 30f * manager.RunUnlocks.Count);
                unlocks.text = "<color=#FFD700>" + string.Join("\n", manager.RunUnlocks) + "</color>";
            }

            TextMeshProUGUI totals = PTUIFactory.CreateLabel("Totals", panel, 20f, TextAlignmentOptions.Left, 110f);
            totals.text = BuildTotals(manager);

            TextMeshProUGUI table = PTUIFactory.CreateLabel("Books", panel, 18f, TextAlignmentOptions.TopLeft, 40f + 26f * manager.runLedger.Count);
            table.text = BuildTable(manager);

            TextMeshProUGUI highlights = PTUIFactory.CreateLabel("Highlights", panel, 20f, TextAlignmentOptions.Center, 56f);
            highlights.text = BuildHighlights(manager);

            TextMeshProUGUI restartLabel;
            PTUIFactory.CreateButton(panel, 46f, manager.RestartRun, out restartLabel);
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
            text.Append("Stolen from you: <color=#FF8080>").Append(manager.TotalStolen).Append("g</color>")
                .Append("<pos=50%>Recovered by audits: ").Append(manager.TotalRecovered).Append("g\n");
            text.Append("Heroes hired: ").Append(manager.runLedger.Count)
                .Append("<pos=50%>Deaths: ").Append(deaths).Append("  ·  Strikes: ").Append(manager.TributeStrikes);
            return text.ToString();
        }

        private static string BuildTable(PTManager manager)
        {
            StringBuilder text = new StringBuilder();
            text.Append("<b>Name<pos=36%>Kills<pos=44%>Looted<pos=54%>Wages<pos=64%>Stole<pos=73%>Fate</b>\n");
            foreach (PTEmployeeRecord record in manager.runLedger)
            {
                string fate = record.IsEmployed ? "Still with you (" + record.Mood + ")" : record.fate;
                text.Append(record.name)
                    .Append("<pos=36%>").Append(record.kills)
                    .Append("<pos=44%>").Append(record.goldLooted).Append("g")
                    .Append("<pos=54%>").Append(record.wagesReceived).Append("g")
                    .Append("<pos=64%>").Append(record.stolenTotal > 0 ? "<color=#FF8080>" + record.stolenTotal + "g</color>" : "-")
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
