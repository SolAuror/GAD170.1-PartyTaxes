using System.Collections.Generic;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Jobs Part: the daily job board. Each morning three contracts are drawn from the pool,
/// each with visible risk and pay; a rest day is always available. On the last day the Goblin King's contract appears.
public partial class PTManager
{
    [Header("Job Board")]
    [Tooltip("Contracts that can appear on the daily board (3 are drawn each morning, weighted). Tuned with Tools/BalanceSim.")]
    public PTJob[] jobPool = PTJob.DefaultPool();
    [Tooltip("The optional last-day boss contract. Win it, then pay the final tribute, to be Vindicated.")]
    public PTJob goblinKingJob = PTJob.GoblinKing();

    private const int JOB_BOARD_SIZE = 3;                                                                    //contracts on the board each morning
    private const float REST_DAY_HEAL_PERCENT = 0.4f;                                                        //a rest day restores 40% of max HP (no pay)

    private readonly List<PTJob> jobBoard = new List<PTJob>();
    public IReadOnlyList<PTJob> JobBoard => jobBoard;
    public PTJob ActiveJob { get; private set; }                                                             //the job being fought right now
    public bool SlewGoblinKing { get; private set; }

    public int JobPay(PTJob job) => job == null ? 0 : job.basePay + job.payPerWeek * ((Mathf.Max(1, daysElapsed) - 1) / 7);

    void RefreshJobBoard()                                                                                   //draw this morning's contracts
    {
        jobBoard.Clear();
        List<PTJob> available = new List<PTJob>();
        foreach (PTJob job in jobPool)
        {
            if (job != null && job.kind == JobKind.Fight && job.minDay <= Mathf.Max(1, daysElapsed) && job.weight > 0f) available.Add(job);
        }
        while (available.Count > 0 && jobBoard.Count < JOB_BOARD_SIZE)                                       //weighted draw without repeats
        {
            float total = 0f;
            foreach (PTJob job in available) total += job.weight;
            float roll = Random.value * total;
            PTJob picked = available[available.Count - 1];
            foreach (PTJob job in available)
            {
                roll -= job.weight;
                if (roll <= 0f) { picked = job; break; }
            }
            jobBoard.Add(picked);
            available.Remove(picked);
        }
        if (daysElapsed >= RUN_LENGTH_DAYS && goblinKingJob != null)                                         //the last day: the King awaits
        {
            jobBoard.Add(goblinKingJob);
        }
    }

    public void TryTakeJob(int index)                                                                        //take a contract from the board
    {
        if (!CanPerformTownAction("You're already in a fight!")) return;
        if (index < 0 || index >= jobBoard.Count) return;
        if (hasFought)
        {
            PTAdventureLog.Log("The party has already worked today. Sleep, and see what tomorrow's board brings.");
            return;
        }
        if (partyMembers.Count == 0)
        {
            PTAdventureLog.Log("You have no one to send! Hire someone at the tavern first.");
            return;
        }
        if (PendingConfrontation != null)
        {
            PTAdventureLog.Log("Decide what to do about " + PendingConfrontation.Name + " first.");
            return;
        }

        PTJob job = jobBoard[index];
        hasFought = true;
        PTAdventureLog.Log("The party takes the contract: " + job.jobName + " (" + JobPay(job) + " gold).");

        if (job.kind == JobKind.Fight && Random.value >= job.ambushChance)                                   //a quiet job: paid without a fight
        {
            PTAdventureLog.Log("Not a goblin in sight. An easy day's work.");
            CompleteJob(job);
            UpdateUI();
            return;
        }

        ActiveJob = job;
        if (job.kind == JobKind.Boss)
        {
            SpawnBoss(job);
            SpawnEnemyGroup(GetEncounterSize(job), GetAveragePartyLevel());                                  //the King's guard
        }
        else
        {
            SpawnEnemyGroup(GetEncounterSize(job), GetAveragePartyLevel() + job.enemyLevelBonus);
        }

        if (adversaries.Count == 0)                                                                          //nothing could be spawned (missing prefabs)
        {
            PTAdventureLog.Log("The hunt turns up nothing today.");
            ActiveJob = null;
            hasFought = false;
            return;
        }

        enemyMsg = "Ambush! " + adversaries.Count + " enemies, led by <enemy>" + adversaries[0].Name + "</enemy>!";
        PTAdventureLog.Log(enemyMsg);
        StartCombat();
    }

    public void TryRestDay()                                                                                 //spend the day resting instead of working
    {
        if (!CanPerformTownAction("You can't rest in the middle of a fight!")) return;
        if (hasFought)
        {
            PTAdventureLog.Log("The party has already worked today.");
            return;
        }
        hasFought = true;
        int totalRested = 0;
        foreach (PTSoul member in partyMembers)
        {
            totalRested += member.RestoreHP(Mathf.RoundToInt(member.maxHP * REST_DAY_HEAL_PERCENT));
        }
        PTAdventureLog.Log("The party takes the day off and recovers " + totalRested + " HP. Wages are still due tonight.");
        UpdateUI();
    }

    void CompleteJob(PTJob job)                                                                              //pay out a finished contract
    {
        int pay = JobPay(job);
        ChangeGold(pay);
        if (job.loyaltyBonus != 0) ChangePartyLoyalty(job.loyaltyBonus);
        if (job.kind == JobKind.Boss)
        {
            SlewGoblinKing = true;
            PTAdventureLog.Log("The Goblin King is dead! Pay the Queen tonight and your name is cleared for good.");
        }
        PTAdventureLog.Log("Contract complete: " + job.jobName + " pays " + pay + " gold. You have " + gold + " gold in total.");
    }
}
