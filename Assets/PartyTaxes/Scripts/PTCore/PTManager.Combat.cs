using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using PartyTaxes;


/// PTManager partial — Combat Part: auto-resolved battles for the day's job. The accountant doesn't swing a sword:
/// you pick a stance in town, then watch, and spend gold on interventions between rounds.
public partial class PTManager
{
    public enum Stance { Aggressive, Balanced, Cautious }
    public enum CombatSpeed { Normal, Fast, Skip }

    [Header("Combat Timing")]
    [SerializeField, Tooltip("Seconds between combat actions at normal speed")]
    private float actionDelay = 0.35f;
    [SerializeField, Tooltip("Pause at the start of each round at normal speed: the window for interventions")]
    private float roundPause = 0.6f;
    [Tooltip("How the party fights. Set in town before taking a job.")]
    public Stance stance = Stance.Balanced;

    private const float AGGRESSIVE_DAMAGE_DEALT = 1.15f;                                                 //Aggressive: hit harder and finish the weakest enemy first...
    private const float AGGRESSIVE_DAMAGE_TAKEN = 1.25f;                                                 //...but take more damage
    private const float CAUTIOUS_DAMAGE_DEALT = 0.85f;                                                   //Cautious: hit softer, take out the biggest hitter first...
    private const float CAUTIOUS_DAMAGE_TAKEN = 0.8f;                                                    //...and take less damage
    private const int POTION_COST = 15;                                                                  //potion toss: heal the most hurt member
    private const int POTION_HEAL = 40;
    private const int HAZARD_PAY_PER_MEMBER = 5;                                                         //hazard pay: +25% damage this round, +2 loyalty each
    private const float HAZARD_PAY_DAMAGE_BONUS = 0.25f;
    private const int HAZARD_PAY_LOYALTY = 2;
    private const int BRIBE_BASE_COST = 15;                                                              //bribe: the toughest (non-boss) enemy leaves; costs 15 + 2x its loot
    private const int BRIBE_LOOT_MULTIPLIER = 2;
    private const float RETREAT_COST_PERCENT = 0.1f;                                                     //sound the retreat: lose 10% of your gold and the job's pay
    private const int RETREAT_COWARD_BELOW_LOYALTY = 50;                                                 //only members with loyalty below this are branded cowards
    private const float FAST_SPEED_MULTIPLIER = 0.4f;                                                    //2x speed shortens every delay to 40%
    private const int MAX_COMBAT_ROUNDS = 100;                                                           //safety valve: the goblins lose interest

    public CombatSpeed Speed { get; private set; } = CombatSpeed.Normal;
    public int CombatRound { get; private set; }
    public bool HazardPaidThisRound { get; private set; }
    public bool PotionUsedThisRound { get; private set; }
    public bool BribedThisRound { get; private set; }
    public bool FocusOrderedThisRound { get; private set; }
    private PTSoul focusTarget;                                                                          //ordered target, attacked by everyone until it falls
    private Coroutine combatRoutine;                                                                     //the running battle
    private int fightLoot = 0;                                                                           //gold looted this fight (loot-share members take a cut of it)

    public void ChangeXP(int xpAmount, string source = null)
    {
        if (partyMembers.Count > 0)
        {
            int xpShare = Mathf.FloorToInt((float)xpAmount / partyMembers.Count);
            foreach (PTSoul member in partyMembers)
            {
                if (member.isAlive)
                {
                    member.GainXP(xpShare);
                    if (!string.IsNullOrEmpty(source))
                    {
                        PTAdventureLog.Log(member.Name + " gained " + xpShare + " XP!");
                    }
                }
            }
        }
    }


    public void OnEncounter(InputAction.CallbackContext context)                                         //Method to handle E key press: open the job board
    {
        if (!context.performed) return;
        if (actionBar != null)
        {
            actionBar.ToggleJobs();                                                                      //pick a job with the mouse
            return;
        }
        if (jobBoard.Count > 0) TryTakeJob(0);                                                           //no action bar: take the first job on the board
    }

    int GetEncounterSize(PTJob job)                                                                      //how many enemies a job spawns: a multiple of party size, plus weekly escalation for jobs that escalate
    {
        int escalation = 0;
        if (job.escalates && daysElapsed >= ESCALATION_START_DAY)
        {
            escalation = (daysElapsed - ESCALATION_START_DAY) / ESCALATION_EVERY_DAYS + 1;               //+1 potential enemy each week from day 8
        }
        int min = Mathf.Max(1, Mathf.CeilToInt(partyCount * job.enemiesMin));
        int max = Mathf.Max(1, Mathf.CeilToInt(partyCount * job.enemiesMax)) + escalation;
        if (job.kind == JobKind.Fight)
        {
            min += ExtraEnemies;                                                                         //Audit Level 5: one more goblin in every job
            max += ExtraEnemies;
        }
        return Random.Range(min, max + 1);                                                               //int Random.Range excludes the max, so +1 to make it inclusive
    }

    void SpawnEnemyGroup(int spawnCount, int enemyLevel)                                                 //spawn a group of enemies around the enemy spawn point
    {
        if (soulGen != null && soulGen.enemyBasePrefab != null)
        {
            Vector3 baseSpawnPosition = enemySpawnPoint != null ?
                                        enemySpawnPoint.position :
                                        transform.position;                                              //use spawn point if assigned, otherwise use PTManager position
            float minDistance = 1.5f;                                                                    //minimum distance between enemies
            int maxAttempts = 30;                                                                        //maximum attempts to find valid position
            int partyLevel = enemyLevel;                                                                 //level used for enemy type selection (party level + the job's bonus)

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 spawnPosition = baseSpawnPosition;

                for (int attempt = 0; attempt < maxAttempts; attempt++)                                  //try to find a non-overlapping position
                {
                    Vector3 randomOffset = Random.insideUnitSphere * 3f;
                    randomOffset.y = 0;
                    spawnPosition = baseSpawnPosition + randomOffset;

                    bool validPosition = true;
                    foreach (PTSoul existingEnemy in adversaries)
                    {
                        if (Vector3.Distance(spawnPosition, existingEnemy.transform.position) < minDistance)
                        {
                            validPosition = false;
                            break;
                        }
                    }
                    if (validPosition) break;
                }

                GameObject enemyObj = soulGen.SpawnEnemy(transform, spawnPosition, partyLevel);          //delegate all type selection and stat generation to PTSoulGen
                if (enemyObj != null)
                {
                    PTSoul enemySoul = enemyObj.GetComponent<PTSoul>();
                    if (enemySoul != null)
                        adversaries.Add(enemySoul);                                                      //add to adversaries list
                }
            }
        }
        else if (enemyPrefabs != null && enemyPrefabs.Length > 0)                                        //legacy fallback: use assigned prefab array if no soulGen
        {
            Vector3 baseSpawnPosition = enemySpawnPoint != null ?
                                        enemySpawnPoint.position :
                                        transform.position;
            float minDistance = 1.5f;
            int maxAttempts = 30;

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 spawnPosition = baseSpawnPosition;
                bool validPosition = false;

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    Vector3 randomOffset = Random.insideUnitSphere * 3f;
                    randomOffset.y = 0;
                    spawnPosition = baseSpawnPosition + randomOffset;

                    validPosition = true;
                    foreach (PTSoul existingEnemy in adversaries)
                    {
                        if (Vector3.Distance(spawnPosition, existingEnemy.transform.position) < minDistance)
                        {
                            validPosition = false;
                            break;
                        }
                    }
                    if (validPosition) break;
                }

                GameObject randomEnemyPrefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
                int avgLevel = GetAveragePartyLevel();
                if (randomEnemyPrefab.GetComponent<PTSoul>().level > avgLevel)
                {
                    System.Collections.Generic.List<GameObject> suitableEnemies = new System.Collections.Generic.List<GameObject>();
                    foreach (GameObject prefab in enemyPrefabs)
                        if (prefab.GetComponent<PTSoul>().level <= avgLevel)
                            suitableEnemies.Add(prefab);
                    if (suitableEnemies.Count > 0)
                        randomEnemyPrefab = suitableEnemies[Random.Range(0, suitableEnemies.Count)];
                }

                GameObject enemyObj = Instantiate(randomEnemyPrefab, spawnPosition, Quaternion.identity, transform);
                enemyObj.name = enemyObj.GetComponent<PTSoul>().Type + (i + 1);
                PTSoul enemySoul = enemyObj.GetComponent<PTSoul>();
                if (enemySoul != null)
                    adversaries.Add(enemySoul);
            }
        }
    }

    void SpawnBoss(PTJob job)                                                                            //the boss leads; its guard is spawned alongside
    {
        int bossLevel = Mathf.Max(job.bossMinLevel, GetAveragePartyLevel() + job.bossLevelBonus);
        int before = adversaries.Count;
        SpawnEnemyGroup(1, bossLevel);
        if (adversaries.Count == before) return;
        PTSoul boss = adversaries[adversaries.Count - 1];
        boss.Name = job.bossName;
        boss.isBoss = true;
        boss.healthMultiplier = job.bossHealthMultiplier;
        boss.attackMultiplier = job.bossAttackMultiplier;
        boss.goldReward *= 3;
        boss.accumulatedLifeXp *= 3;
        boss.FinishSetup();                                                                              //recalculate stats with the boss multipliers and fill HP
        boss.transform.localScale = boss.transform.localScale * 1.5f;                                    //make the king look the part
        boss.gameObject.name = boss.Name;
    }

#region Combat flow
    private void StartCombat()                                                                           //begin the auto-resolved battle
    {
        State = GameState.Combat;                                                                        //town actions and sleeping are blocked until the battle ends
        CombatRound = 0;
        focusTarget = null;
        fightLoot = 0;
        foreach (PTSoul member in partyMembers)
        {
            if (member.employment != null) member.employment.killsThisFight = 0;
        }
        PTAdventureLog.Log("=== COMBAT BEGINS ===  (" + stance + " stance)");
        UpdateUI();
        combatRoutine = StartCoroutine(RunCombat());
    }

    private System.Collections.IEnumerator RunCombat()                                                   //rounds play out automatically; the player intervenes with gold between actions
    {
        while (true)
        {
            CombatRound++;
            HazardPaidThisRound = false;
            PotionUsedThisRound = false;
            BribedThisRound = false;
            FocusOrderedThisRound = false;
            PTAdventureLog.Log("─── Round " + CombatRound + " ───");
            UpdateUI();

            float pause = ScaledDelay(roundPause);
            if (pause > 0f) yield return new WaitForSeconds(pause);                                      //the window for interventions
            if (CheckCombatOver()) yield break;

            // initiative: the side with the higher average Sense acts first (party wins ties)
            float partyAvg = partyMembers.Count > 0 ? (float)partyMembers.Average(m => m.atrSense) : 0f;
            float enemyAvg = adversaries.Count > 0 ? (float)adversaries.Average(e => e.atrSense) : 0f;
            bool enemiesFirst = enemyAvg > partyAvg;

            for (int phase = 0; phase < 2; phase++)
            {
                bool partyPhase = (phase == 0) != enemiesFirst;
                List<PTSoul> order = (partyPhase ? partyMembers : adversaries).OrderByDescending(s => s.atrSense).ToList();
                foreach (PTSoul actor in order)
                {
                    if (CheckCombatOver()) yield break;
                    List<PTSoul> side = partyPhase ? partyMembers : adversaries;
                    if (actor == null || !actor.isAlive || !side.Contains(actor)) continue;              //died, was bribed away, or left earlier this round
                    if (partyPhase) ProcessPartyAttack(actor);
                    else ProcessEnemyAttack(actor);
                    UpdateUI();
                    float delay = ScaledDelay(actionDelay);
                    if (delay > 0f) yield return new WaitForSeconds(delay);
                }
            }
            if (CheckCombatOver()) yield break;
            if (CombatRound >= MAX_COMBAT_ROUNDS)
            {
                PTAdventureLog.Log("The goblins lose interest and scatter into the hills.");
                EndCombatWithoutVictory(false);
                yield break;
            }
        }
    }

    private bool CheckCombatOver()                                                                       //ends the battle if one side is gone; returns true if it ended
    {
        if (State != GameState.Combat) return true;
        if (partyMembers.Count == 0)
        {
            ResetCombatState();
            TriggerGameOver("You've been slaughtered by the enemy!", "Slain");
            return true;
        }
        if (adversaries.Count == 0)
        {
            OnAllEnemiesDefeated();
            return true;
        }
        return false;
    }

    private float ScaledDelay(float seconds)                                                             //combat speed setting applied to a delay
    {
        switch (Speed)
        {
            case CombatSpeed.Fast: return seconds * FAST_SPEED_MULTIPLIER;
            case CombatSpeed.Skip: return 0f;
            default: return seconds;
        }
    }

    private void ResetCombatState()                                                                      /// Resets all combat state. Call when combat ends for any reason.
    {
        if (combatRoutine != null)
        {
            StopCoroutine(combatRoutine);
            combatRoutine = null;
        }
        focusTarget = null;
        ActiveJob = null;
    }

    private bool InBossFight => adversaries.Exists(e => e.isBoss);
#endregion

#region Stances and targeting
    private float PartyDamageMultiplier
    {
        get
        {
            float multiplier = stance == Stance.Aggressive ? AGGRESSIVE_DAMAGE_DEALT : stance == Stance.Cautious ? CAUTIOUS_DAMAGE_DEALT : 1f;
            return HazardPaidThisRound ? multiplier * (1f + HAZARD_PAY_DAMAGE_BONUS) : multiplier;
        }
    }

    private float StanceDamageTaken => stance == Stance.Aggressive ? AGGRESSIVE_DAMAGE_TAKEN : stance == Stance.Cautious ? CAUTIOUS_DAMAGE_TAKEN : 1f;

    private PTSoul PickTarget()                                                                          //who the party attacks: an ordered target first, otherwise by stance
    {
        if (focusTarget != null && adversaries.Contains(focusTarget)) return focusTarget;
        switch (stance)
        {
            case Stance.Aggressive: return adversaries.OrderBy(e => e.currentHP).First();                //finish off the weakest
            case Stance.Cautious: return adversaries.OrderByDescending(e => e.attack).First();           //take out the biggest hitter
            default: return adversaries[Random.Range(0, adversaries.Count)];
        }
    }

    public void SetStance(Stance newStance)                                                              //change stance (town only)
    {
        if (State == GameState.Combat)
        {
            PTAdventureLog.Log("Too late to change tactics, the fight has started!");
            return;
        }
        stance = newStance;
        UpdateUI();
    }

    public void CycleStance()
    {
        SetStance(stance == Stance.Aggressive ? Stance.Balanced : stance == Stance.Balanced ? Stance.Cautious : Stance.Aggressive);
    }

    public void CycleCombatSpeed()                                                                       //normal -> fast -> skip -> normal
    {
        Speed = Speed == CombatSpeed.Normal ? CombatSpeed.Fast : Speed == CombatSpeed.Fast ? CombatSpeed.Skip : CombatSpeed.Normal;
        UpdateUI();
    }
#endregion

#region Attacks
    private bool ProcessPartyAttack(PTSoul attacker)                                        ///against a random enemy, processes the result immediately, then resumes AdvanceToNextPartyTurn.
    {
        if (adversaries.Count == 0) return true;

        PTSoul target = PickTarget();
        int damage = Mathf.RoundToInt(attacker.DealDamage() * PartyDamageMultiplier * TraitDamageMultiplier(attacker)); //stance, hazard pay and traits change how hard the party hits
        bool wasCrit = attacker.WasCrit;
        int actualDamage = target.TakeDamage(damage);
        string critTag = wasCrit ? " <color=" + PTUITheme.RedInk + "><b>CRITICAL HIT!</b></color>" : "";

        PTAdventureLog.Log(attacker.Name + " attacks " +
                           "<enemy>" + target.Name + "</enemy>" + " for " + actualDamage + " damage!" + critTag + " " +
                           "<enemy>" + target.Name + "</enemy>" + " has " +
                           target.currentHP + "/" + target.maxHP + " HP remaining.");

        if (!target.isAlive)
        {
            PTAdventureLog.Log("<enemy>" + target.Name + "</enemy>" + " has been slain by " + attacker.Name + "!");
            int loot = TraitLoot(attacker, target.goldReward);                                           //Light-fingered members find more
            if (attacker.employment != null)                                                             //credit the kill in their employment file
            {
                attacker.employment.kills++;
                attacker.employment.killsThisFight++;
                attacker.employment.goldLooted += loot;
                attacker.employment.ChangeLoyalty(LOYALTY_KILL);
            }
            fightLoot += loot;
            ChangeGold(loot);
            ChangeXP(target.accumulatedLifeXp, "<enemy>" + target.Name + "</enemy>");
            PTAdventureLog.Log("<enemy>" + target.Name + "</enemy>" + " has been slain! You found " + loot + " gold on the corpse.");
            if (focusTarget == target) focusTarget = null;
            Destroy(target.gameObject);
            adversaries.Remove(target);
        }

        return adversaries.Count == 0;
    }


    private void ProcessEnemyAttack(PTSoul enemyAttacker)                                       /// Executes one enemy's automatic attack, processes the result immediately.
    {
        // Filter for party members still in combat (alive and haven't fled)
        List<PTSoul> availableTargets = partyMembers.Where(m => m.isAlive).ToList();                         //party members still standing

        if (availableTargets.Count == 0) return;                     // no one left to target (all fled or dead)

        PTSoul partyTarget = availableTargets[Random.Range(0, availableTargets.Count)];
        int enemyDamage = Mathf.RoundToInt(enemyAttacker.DealDamage() * StanceDamageTaken);                 //an aggressive stance leaves the party more exposed
        bool wasCrit = enemyAttacker.WasCrit;
        int actualDamage = partyTarget.TakeDamage(enemyDamage);
        string critTag = wasCrit ? " <color=" + PTUITheme.RedInk + "><b>CRITICAL HIT!</b></color>" : "";

        PTAdventureLog.Log("<enemy>" + enemyAttacker.Name + "</enemy>" + " attacks " +
                           partyTarget.Name + " for " + actualDamage + " damage!" + critTag + " " +
                           partyTarget.Name + " has " +
                           partyTarget.currentHP + "/" + partyTarget.maxHP + " HP remaining.");

        if (!partyTarget.isAlive)
        {
            PTAdventureLog.Log(partyTarget.Name + " has been slain by " + enemyAttacker.Name + "!");

            List<PTSoul> livingEnemies = adversaries.Where(a => a.isAlive).ToList();
            if (livingEnemies.Count > 0)
            {
                int xpShare = Mathf.FloorToInt((float)partyTarget.accumulatedLifeXp / livingEnemies.Count);
                foreach (PTSoul adv in livingEnemies)
                {
                    adv.GainXP(xpShare);
                    PTAdventureLog.Log("<enemy>" + adv.Name + "</enemy>" + " gained " + xpShare + " XP for slaying " + partyTarget.Name + "!");
                }
            }
            ChangePartyLoyalty(LOYALTY_ALLY_DIED, partyTarget);                                          //losing a comrade shakes everyone
            RemovePartyMember(partyTarget, EmploymentStatus.Slain, "Slain by " + enemyAttacker.Name + " on day " + daysElapsed);
        }
    }
#endregion

#region Interventions (gold spent mid-battle)
    public int HazardPayCost => partyMembers.Count * HAZARD_PAY_PER_MEMBER;
    public int PotionCost => POTION_COST;
    public int RetreatCost => Mathf.FloorToInt(gold * RETREAT_COST_PERCENT);
    public PTSoul BribeTarget => adversaries.Where(e => !e.isBoss).OrderByDescending(e => e.attack).FirstOrDefault();
    public int BribeCost => BribeTarget != null ? BRIBE_BASE_COST + BribeTarget.goldReward * BRIBE_LOOT_MULTIPLIER : 0;
    public PTSoul PotionTarget => partyMembers.OrderBy(m => (float)m.currentHP / Mathf.Max(1, m.maxHP)).FirstOrDefault();
    public PTSoul FocusCandidate => adversaries.OrderByDescending(e => e.attack).FirstOrDefault();
    public bool CanRetreat => State == GameState.Combat && !InBossFight;

    bool CanIntervene(bool usedThisRound, int cost, string what)                                         //common checks for interventions
    {
        if (State != GameState.Combat) return false;
        if (usedThisRound)
        {
            PTAdventureLog.Log("You've already ordered " + what + " this round.");
            return false;
        }
        if (gold < cost)
        {
            PTAdventureLog.Log("You can't afford " + what + " (" + cost + " gold).");
            return false;
        }
        return true;
    }

    public void TryHazardPay()                                                                           //+25% party damage this round
    {
        int cost = HazardPayCost;
        if (!CanIntervene(HazardPaidThisRound, cost, "hazard pay")) return;
        ChangeGold(-cost);
        HazardPaidThisRound = true;
        ChangePartyLoyalty(HAZARD_PAY_LOYALTY);
        PTAdventureLog.Log("You wave a fistful of coins: hazard pay! The party hits harder this round. (-" + cost + " gold)");
        UpdateUI();
    }

    public void TryPotion()                                                                              //heal the most hurt party member
    {
        PTSoul patient = PotionTarget;
        if (patient == null || !CanIntervene(PotionUsedThisRound, POTION_COST, "a potion")) return;
        ChangeGold(-POTION_COST);
        PotionUsedThisRound = true;
        int healed = patient.RestoreHP(Mathf.RoundToInt(POTION_HEAL * (1f + PotionBonus)));              //a Medic makes potions go further
        PTAdventureLog.Log("You lob a potion to " + patient.Name + ", restoring " + healed + " HP. (-" + POTION_COST + " gold)");
        UpdateUI();
    }

    public void TryBribe()                                                                               //pay the toughest (non-boss) enemy to leave; no loot or XP from it
    {
        PTSoul target = BribeTarget;
        if (target == null)
        {
            if (InBossFight) PTAdventureLog.Log("The Goblin King can't be bought.");
            return;
        }
        int cost = BribeCost;
        if (!CanIntervene(BribedThisRound, cost, "a bribe")) return;
        ChangeGold(-cost);
        BribedThisRound = true;
        if (focusTarget == target) focusTarget = null;
        adversaries.Remove(target);
        Destroy(target.gameObject);
        PTAdventureLog.Log("<enemy>" + target.Name + "</enemy> pockets " + cost + " gold and wanders off.");
        if (adversaries.Count == 0) OnAllEnemiesDefeated();                                              //bribed the last one: the job's done
        UpdateUI();
    }

    public void TryFocusFire()                                                                           //everyone attacks the biggest threat until it falls (free, once per round)
    {
        PTSoul target = FocusCandidate;
        if (target == null || !CanIntervene(FocusOrderedThisRound, 0, "focus fire")) return;
        FocusOrderedThisRound = true;
        focusTarget = target;
        PTAdventureLog.Log("You point your quill at <enemy>" + target.Name + "</enemy>. Everyone focus on that one!");
        UpdateUI();
    }

    public void TryRetreat()                                                                             //the whole party withdraws: no pay, some gold lost, the disloyal are branded cowards
    {
        if (State != GameState.Combat) return;
        if (InBossFight)
        {
            PTAdventureLog.Log("The doors of the Goblin King's hall have slammed shut. There is no retreat!");
            return;
        }
        int cost = RetreatCost;
        ChangeGold(-cost);
        foreach (PTSoul member in partyMembers)
        {
            if (!member.markedByDeath && member.employment != null && member.employment.loyalty < RETREAT_COWARD_BELOW_LOYALTY)
            {
                member.isCowardly = true;                                                                //the ones who didn't believe in you ran first
            }
        }
        PTAdventureLog.Log("You sound the retreat! The party scrambles away, dropping " + cost + " gold. Those with little loyalty ran first and are branded Cowardly.");
        EndCombatWithoutVictory(true);
    }
#endregion

#region Player Input Handlers
    public void OnAttack(InputAction.CallbackContext context)                                            //Left click: combat is automatic now, so clicks are left to the UI
    {
    }

    public void OnRunAway(InputAction.CallbackContext context)                                           //Method to handle R key press - sound the retreat
    {
        if (context.performed) TryRetreat();
    }
#endregion

#region Combat endings
    private void OnAllEnemiesDefeated()                                                                  //the job is done: get paid
    {
        PTJob job = ActiveJob;
        ResetCombatState();
        SetSunIntensity(0.1f);
        State = GameState.Town;
        PTAdventureLog.Log("You have defeated all the enemies! You can now sleep through the night.");
        foreach (PTSoul member in partyMembers)                                                          //Bloodthirsty members sulk if they didn't get a kill
        {
            if (member.employment != null && member.employment.HasSpecial("bloodthirsty") && member.employment.killsThisFight == 0)
            {
                member.employment.ChangeLoyalty(BLOODTHIRSTY_NO_KILL_LOYALTY);
                PTAdventureLog.Log(member.Name + " didn't get a kill and is in a foul mood.");
            }
        }
        if (job != null) CompleteJob(job);
        UpdateUI();
    }

    private void EndCombatWithoutVictory(bool retreated)                                                 //the party withdrew (or the enemy scattered): no pay for the job
    {
        ResetCombatState();
        NextJobPayPercent = 0;                                                                           //any event bonus on this job is lost
        for (int i = adversaries.Count - 1; i >= 0; i--)
        {
            Destroy(adversaries[i].gameObject);
        }
        adversaries.Clear();
        SetSunIntensity(0.1f);
        State = GameState.Town;
        if (retreated) PTAdventureLog.Log("No loot and no pay today. You can still sleep through the night.");
        UpdateUI();
    }
#endregion
}
