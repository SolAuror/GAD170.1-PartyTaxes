using System.Collections.Generic;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Run Part: starting a run (accusation, Audit Level, seed), difficulty modifiers,
/// and recording the result for unlocks between runs.
public partial class PTManager
{
    public Accusation CurrentAccusation { get; private set; } = Accusation.Clerk;
    public int AuditLevel { get; private set; } = 1;
    public int RunSeed { get; private set; }
    public bool IsDailyRun { get; private set; }
    public int ThievesCaughtThisRun { get; private set; }
    public List<string> RunUnlocks { get; private set; } = new List<string>();                              //what this run unlocked, shown in the final ledger
    private bool runStarted = false;
    private bool runRecorded = false;

    public float TributeMultiplier => PTRunRules.TributeMultiplier(AuditLevel, CurrentAccusation);
    public float TheftMultiplier => PTRunRules.TheftMultiplier(AuditLevel);
    public float FeeMultiplier => PTRunRules.FeeMultiplier(AuditLevel);
    public int ExtraEnemies => PTRunRules.ExtraEnemies(AuditLevel);

    /// Starts the run: called by the setup screen, or straight from Start() when there's no action bar.
    public void BeginRun(Accusation accusation, int auditLevel, int seed, bool dailyRun)
    {
        if (runStarted) return;
        runStarted = true;
        CurrentAccusation = accusation;
        AuditLevel = Mathf.Clamp(auditLevel, 1, PTRunRules.MAX_AUDIT_LEVEL);
        RunSeed = seed;
        IsDailyRun = dailyRun;
        Random.InitState(seed);                                                                              //same seed, same recruits, jobs and events

        gold += PTRunRules.StartingGoldBonus(accusation);
        SetSunIntensity(2f);
        for (int i = 0; i < startingPartySize; i++)                                                          //spawn the starting party
        {
            bool scapegoatVeteran = accusation == Accusation.Scapegoat && i == 0;                            //the Scapegoat starts with a Loyal Veteran who owes them
            SpawnRandomPartyMember(scapegoatVeteran ? new List<string> { "loyal", "veteran" } : null, !scapegoatVeteran);
        }

        RefreshTavern();                                                                                     //put the first candidates on the tavern board
        RefreshJobBoard();                                                                                   //and the first contracts on the job board
        PTAdventureLog.Log(PTRunRules.Name(accusation) + ", Audit Level " + AuditLevel + (dailyRun ? " (daily run)" : "") + ". " + PTRunRules.Description(accusation));
        PTAdventureLog.Log("By order of the Queen: you will remit " + NextTributeAmount + " gold to her auditor at the end of day " + NextTributeDay +
                           ", and every week after, for " + RUN_LENGTH_DAYS + " days. Pay, and be pardoned. Fail twice, and hang.");
        dailyMsg = "Heil, <partyname>" + partyName + "</partyname>" +                                       //greet the party,
                   "! You have " + gold +                                                                    //display the party gold
                   " gold and " + partyMembers.Count +                                                       //display the number of party members
                   " party members. Daily Wages are " + CalculateTotalDailyWages() + ".";                   //display the total daily wages
        daysElapsed++;                                                                                       //increment the day
        State = GameState.Town;
        PTAdventureLog.Log(dailyMsg);                                                                        //print the daily message
        UpdateUI();                                                                                          //update UI with initial values
    }

    void RecordRunEnd(bool won)                                                                              //save progress between runs, once
    {
        if (runRecorded || !runStarted) return;
        runRecorded = true;
        RunUnlocks = PTRunRules.RecordRun(won, AuditLevel, daysElapsed, ThievesCaughtThisRun, RunScore);
    }
}
