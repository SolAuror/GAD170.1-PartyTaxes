using System.Linq;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Tribute Part: the Queen's weekly audit, strikes, and the pardon after 28 days.
/// The tribute is collected at the end of every 7th day, before wages: the Queen gets paid first.
public partial class PTManager
{
    private const int RUN_LENGTH_DAYS = 28;                                                                  //survive and pay the final tribute to be pardoned
    private const int TRIBUTE_INTERVAL_DAYS = 7;                                                             //the auditor arrives every 7th day
    private static readonly int[] TRIBUTE_SCHEDULE = { 200, 350, 500, 700 };                                 //gold owed each week
    private const int STRIKES_TO_EXECUTION = 2;                                                              //first missed tribute: seizure; second: the noose
    private const int VICTORY_SCORE_BONUS = 250;
    private const int SCORE_PER_SURVIVOR = 25;
    private const int SCORE_PER_DEATH = 25;

    public int TributeStrikes { get; private set; }
    public int TotalTributePaid { get; private set; }
    public string RunOutcome { get; private set; } = "";
    public int StrikesToExecution => STRIKES_TO_EXECUTION;
    public int RunLengthDays => RUN_LENGTH_DAYS;
    public bool IsTributeDay => daysElapsed % TRIBUTE_INTERVAL_DAYS == 0;
    public int NextTributeDay => ((daysElapsed - 1) / TRIBUTE_INTERVAL_DAYS + 1) * TRIBUTE_INTERVAL_DAYS;   //days 1-7 owe on day 7, 8-14 on day 14...
    public int DaysUntilTribute => NextTributeDay - daysElapsed;

    public int NextTributeAmount
    {
        get
        {
            int week = Mathf.Clamp(NextTributeDay / TRIBUTE_INTERVAL_DAYS - 1, 0, TRIBUTE_SCHEDULE.Length - 1);
            return TRIBUTE_SCHEDULE[week];
        }
    }

    public string TributeStatus => "Tribute: " + NextTributeAmount + "g due " +
                                   (DaysUntilTribute == 0 ? "tonight" : "in " + DaysUntilTribute + " day" + (DaysUntilTribute == 1 ? "" : "s")) +
                                   "  ·  Strikes " + TributeStrikes + "/" + STRIKES_TO_EXECUTION;

    bool CollectTribute()                                                                                    //called at the end of a tribute day; returns false if the run ended
    {
        int due = NextTributeAmount;
        if (gold >= due)
        {
            ChangeGold(-due);
            TotalTributePaid += due;
            PTAdventureLog.Log("The Queen's auditor counts out " + due + " gold and leaves satisfied.");
            if (daysElapsed >= RUN_LENGTH_DAYS)
            {
                TriggerVictory("Four tributes paid in full. The Queen grudgingly signs your pardon, and you're free to go home.");
                return false;
            }
            return true;
        }

        TributeStrikes++;
        if (TributeStrikes >= STRIKES_TO_EXECUTION || daysElapsed >= RUN_LENGTH_DAYS)
        {
            TriggerGameOver("You came up short for the Queen" + (TributeStrikes >= STRIKES_TO_EXECUTION ? " a second time" : " on the final audit") +
                            ". The auditor has you hanged for embezzlement.", "Hanged");
            return false;
        }

        int seized = gold;
        ChangeGold(-gold);
        PTSoul collateral = partyMembers.OrderByDescending(m => m.dailywages).FirstOrDefault();
        PTAdventureLog.Log("You're " + (due - seized) + " gold short! The auditor seizes all " + seized + " gold" +
                           (collateral != null ? " and drags " + collateral.Name + " off in chains as collateral." : ".") +
                           " Fail the Queen again and you hang.");
        if (collateral != null)
        {
            RemovePartyMember(collateral, EmploymentStatus.Seized, "Seized by the Queen's auditor on day " + daysElapsed);
        }
        return true;
    }

    void TriggerVictory(string message)                                                                      //the run is won
    {
        PTAdventureLog.Log(message);
        RunOutcome = "Pardoned";
        State = GameState.Victory;
        UpdateUI();
    }

    public int RunScore                                                                                      //tribute paid + gold kept + survivors - deaths (+ bonus for a pardon)
    {
        get
        {
            int deaths = runLedger.Sum(r => r.timesDied);
            int survivors = partyMembers.Count;
            return TotalTributePaid + gold + survivors * SCORE_PER_SURVIVOR - deaths * SCORE_PER_DEATH +
                   (State == GameState.Victory ? VICTORY_SCORE_BONUS : 0);
        }
    }
}
