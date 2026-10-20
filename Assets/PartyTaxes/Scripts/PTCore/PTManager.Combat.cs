using System.Collections.Generic;                                                       //for using lists and dictionaries to manage party members, enemies, and kill credit
using System.Linq;                                                                      //for using LINQ, which makes sorting and filtering lists much easier and cleaner
using UnityEngine;
using UnityEngine.InputSystem;
using PartyTaxes;


/// PTManager partial — Combat Part: Encounter spawning, attack rounds, and flee logic.
public partial class PTManager
{
    [Header("Combat Timing")]
    [SerializeField, Tooltip("Delay in seconds between each enemy attack")]
    private float enemyAttackDelay = 0.3f;


    private List<PTSoul> partyTurnQueue = new List<PTSoul>();                            // party members sorted by atrSense for the current round
    private int partyTurnIndex = 0;                                             // current position in the party queue
    private bool enemyGoesFirst = false;                                         // true when enemies have higher avg atrSense
    private bool awaitingPartyInput = false;                                        // true when waiting for the player to press Attack
    private PTSoul activeActor = null;                                          // the party member whose turn it currently is

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

    public void OnEncounter(InputAction.CallbackContext context)                                         //Method to handle E key press, using new InputActions system
    {
        if (context.performed) TryEncounter();
    }

    public void TryEncounter()                                                                           //Head out on the day's hunt: spawn a scaled group of enemies and start combat
    {
        if (State == GameState.GameOver) return;                                                         //block input after game over
        if (State == GameState.Combat)
        {
            PTAdventureLog.Log("You are already fighting an enemy!");                                    //log already fighting message
            return;
        }
        if (hasFought)                                                                                   // Only allow one encounter per day
        {
            PTAdventureLog.Log("You have already fought for the day, " +
                               "there are no enemies to fight!");                                        //log already fought message
            return;
        }

        int spawnCount = GetEncounterSize();                                                             //number of enemies to spawn, scaled by party size and days elapsed

        if (soulGen != null && soulGen.enemyBasePrefab != null)
        {
            Vector3 baseSpawnPosition = enemySpawnPoint != null ?
                                        enemySpawnPoint.position :
                                        transform.position;                                           //use spawn point if assigned, otherwise use PTManager position
            float minDistance = 1.5f;                                                                 //minimum distance between enemies
            int maxAttempts = 30;                                                                     //maximum attempts to find valid position
            int partyLevel = GetAveragePartyLevel();                                                 //current average party level for type selection

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 spawnPosition = baseSpawnPosition;

                for (int attempt = 0; attempt < maxAttempts; attempt++)                               //try to find a non-overlapping position
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

                GameObject enemyObj = soulGen.SpawnEnemy(transform, spawnPosition, partyLevel);       //delegate all type selection and stat generation to PTSoulGen
                if (enemyObj != null)
                {
                    PTSoul enemySoul = enemyObj.GetComponent<PTSoul>();
                    if (enemySoul != null)
                        adversaries.Add(enemySoul);                                                   //add to adversaries list
                }
            }
        }
        else if (enemyPrefabs != null && enemyPrefabs.Length > 0)                                     //legacy fallback: use assigned prefab array if no soulGen
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

        if (adversaries.Count == 0)                                                                      //nothing could be spawned (missing prefabs), don't use up the day's encounter
        {
            PTAdventureLog.Log("The hunt turns up nothing today.");
            return;
        }

        hasFought = true;                                                                                //set hasFought to true for the day
        enemyMsg = "An enemy has appeared! There are now " + adversaries.Count +
                   " " + "<enemy>" + adversaries[0].Name + "</enemy>" + "s to fight!";                   //sets the enemy message
        PTAdventureLog.Log(enemyMsg);                                                                    //print enemy message
        StartCombat();                                                                                   //initialise turn queue and begin first turn
    }

    int GetEncounterSize()                                                                               //how many enemies the next encounter spawns
    {
        if (daysElapsed <= ENCOUNTER_GRACE_DAYS) return partyCount;                                      //gentle start: one enemy per party member
        int escalation = daysElapsed < ESCALATION_START_DAY
            ? 0
            : ((daysElapsed - ESCALATION_START_DAY) / ESCALATION_EVERY_DAYS) + 1;                        //+1 potential enemy every 3 days from day 7
        int spawnMax = Mathf.CeilToInt(partyCount * ENCOUNTER_MAX_MULTIPLIER) + escalation;
        return Random.Range(partyCount, spawnMax + 1);                                                   //int Random.Range excludes the max, so +1 to make it inclusive
    }

#region Combat State Management
    private void StartCombat()
    {
        State = GameState.Combat;                                                                        //town actions and sleeping are blocked until the battle ends
        awaitingPartyInput = false;
        activeActor = null;
        PTAdventureLog.Log("=== COMBAT BEGINS ===");
        UpdateUI();
        StartRound();
    }

    private void ResetCombatState()                                                             /// Resets all combat state. Call when combat ends for any reason.
    {
        partyTurnQueue.Clear();
        partyTurnIndex = 0;
        awaitingPartyInput = false;
        activeActor = null;
        UnhideAllFledMembers();                                                         // Unhide fled members when combat ends
    }

    private bool AreAllPartyMembersDead()                                               //Method for checking if party is dead, not just fled - used to determine if game over should trigger instead of just combat end
    {
        return partyMembers.Count == 0 || partyMembers.All(m => !m.isAlive);
    }

    private bool AreNoPartyMembersInCombat()                                            //Method for checking if party is dead or has fled - used to determine if combat should end without victory
    {
        return partyMembers.Count(m => m.isAlive && !m.hasFled) == 0;
    }

    private void UnhideAllFledMembers()                                                 //Method for on combat end or when returning to town, unhide any members who fled and reset their fled status
    {
        foreach (PTSoul member in partyMembers)
        {
            if (member.hasFled)
            {
                member.hasFled = false;
                member.gameObject.SetActive(true);
            }
        }
    }
#endregion

#region Combat Phases
    private void StartRound()                                                   /// determines initiative by average atrSense, then either runs the enemy phase first (auto) or starts the party phase (waiting for player input)
    {

        // Build party turn order (highest sense first) — exclude fled members
        partyTurnQueue = partyMembers.Where(m => m.isAlive && !m.hasFled)
                                     .OrderByDescending(m => m.atrSense)
                                     .ToList();
        partyTurnIndex = 0;

        // Determine phase order by average atrSense
        float partyAvg = partyMembers.Count > 0 ? (float)partyMembers.Sum(m => m.atrSense) / partyMembers.Count : 0f;
        float enemyAvg = adversaries.Count > 0 ? (float)adversaries.Sum(e => e.atrSense) / adversaries.Count : 0f;
        enemyGoesFirst = enemyAvg > partyAvg;                       // party wins on tie

        if (debugMode)
            Debug.Log("Round start — Party avg Sense: " + partyAvg + " | Enemy avg Sense: " + enemyAvg +
                      " → " + (enemyGoesFirst ? "Enemies" : "Party") + " go first.");

        if (enemyGoesFirst)
        {
            PTAdventureLog.Log("=== ENEMY PHASE ===");
            StartCoroutine(RunEnemyPhase());
        }
        else
        {
            // Begin party phase immediately if enemies don't go first
            PTAdventureLog.Log("=== PARTY PHASE ===");
            AdvancePartyTurn();
        }
    }


    private void OnEnemyPhaseComplete()                                                 /// Called after enemy phase completes to continue to party phase.
    {
        UpdateUI();

        // Check if no party members are left in combat
        if (AreNoPartyMembersInCombat())
        {
            if (AreAllPartyMembersDead())
            {
                ResetCombatState();
                TriggerGameOver("You've been slaughtered by the enemy!");
            }
            else
            {
                OnCombatEndedWithoutVictory();
            }
            return;
        }

        // Begin party phase
        PTAdventureLog.Log("=== PARTY PHASE ===");
        AdvancePartyTurn();
    }


    private void OnMidRoundEnemyPhaseComplete()                                             /// Called after enemy phase in mid-round to check game state.
    {
        UpdateUI();

        // Check if no party members are left in combat
        if (AreNoPartyMembersInCombat())
        {
            if (AreAllPartyMembersDead())
            {
                ResetCombatState();
                TriggerGameOver("You've been slaughtered by the enemy!");
            }
            else
            {
                OnCombatEndedWithoutVictory();
            }
            return;
        }

        if (adversaries.Count == 0)
        {
            OnAllEnemiesDefeated();
            return;
        }

        // ── Start the next round ──────────────────────────────────
        PTAdventureLog.Log("─── New Round ───");
        StartRound();
    }

    private void AdvancePartyTurn()                                                     /// Advances to the next living party member's turn, or ends the round if the party phase is complete.
    {
        // Find next living party member
        while (partyTurnIndex < partyTurnQueue.Count)
        {
            PTSoul actor = partyTurnQueue[partyTurnIndex];
            partyTurnIndex++;

            if (!actor.isAlive) continue;                            // skip anyone who died this round
            if (adversaries.Count == 0) break;                       // all enemies dead, skip remaining

            // Pause and wait for player input
            activeActor = actor;
            awaitingPartyInput = true;
            PTAdventureLog.Log(">>> It is " + actor.Name + "'s turn! Press Attack or Run Away.");
            UpdateUI();
            return;
        }

        //Party phase complete
        if (adversaries.Count == 0) { OnAllEnemiesDefeated(); return; }

        // Check if no party members are left in combat (dead or fled)
        if (AreNoPartyMembersInCombat())
        {
            if (AreAllPartyMembersDead())
            {
                ResetCombatState();
                TriggerGameOver("You've been slaughtered by the enemy!");
            }
            else
            {
                // Everyone fled, end combat without rewards
                OnCombatEndedWithoutVictory();
            }
            return;
        }

        if (!enemyGoesFirst)                                         // enemies haven't run yet this round
        {
            PTAdventureLog.Log("=== ENEMY PHASE ===");
            StartCoroutine(RunEnemyPhaseAndContinue());
            return;
        }

        // If enemies went first, continue to next round
        if (adversaries.Count == 0) { OnAllEnemiesDefeated(); return; }
        if (AreAllPartyMembersDead()) { ResetCombatState(); TriggerGameOver("You've been slaughtered by the enemy!"); return; }

        // ── Start the next round ──────────────────────────────────
        PTAdventureLog.Log("─── New Round ───");
        StartRound();
    }


    private System.Collections.IEnumerator RunEnemyPhase()                              /// Runs all living enemies in atrSense order automatically with delays between attacks.
    {
        List<PTSoul> enemyOrder = adversaries.Where(e => e.isAlive)
                                             .OrderByDescending(e => e.atrSense)
                                             .ToList();

        for (int i = 0; i < enemyOrder.Count; i++)
        {
            PTSoul enemy = enemyOrder[i];
            if (!enemy.isAlive) continue;                         // may have died during this phase
            if (AreNoPartyMembersInCombat()) break;                  // stop if no one left in combat

            ProcessEnemyAttack(enemy);

            if (AreNoPartyMembersInCombat()) break;

            // Add delay between enemy attacks (except after the last one)
            if (i < enemyOrder.Count - 1 && enemyAttackDelay > 0)
            {
                yield return new WaitForSeconds(enemyAttackDelay);
            }
        }

        OnEnemyPhaseComplete();
    }


    private System.Collections.IEnumerator RunEnemyPhaseAndContinue()                      /// Runs enemy phase and continues to next round after completion.
    {
        List<PTSoul> enemyOrder = adversaries.Where(e => e.isAlive)
                                             .OrderByDescending(e => e.atrSense)
                                             .ToList();

        for (int i = 0; i < enemyOrder.Count; i++)
        {
            PTSoul enemy = enemyOrder[i];
            if (!enemy.isAlive) continue;
            if (AreNoPartyMembersInCombat()) break;

            ProcessEnemyAttack(enemy);

            if (AreNoPartyMembersInCombat()) break;

            if (i < enemyOrder.Count - 1 && enemyAttackDelay > 0)
            {
                yield return new WaitForSeconds(enemyAttackDelay);
            }
        }

        OnMidRoundEnemyPhaseComplete();
    }


    private bool ProcessPartyAttack(PTSoul attacker)                                        ///against a random enemy, processes the result immediately, then resumes AdvanceToNextPartyTurn.
    {
        if (adversaries.Count == 0) return true;

        PTSoul target = adversaries[Random.Range(0, adversaries.Count)];
        int damage = attacker.DealDamage();
        bool wasCrit = attacker.WasCrit;
        int actualDamage = target.TakeDamage(damage);
        string critTag = wasCrit ? " <color=yellow>CRITICAL HIT!</color>" : "";

        PTAdventureLog.Log(attacker.Name + " attacks " +
                           "<enemy>" + target.Name + "</enemy>" + " for " + actualDamage + " damage!" + critTag + " " +
                           "<enemy>" + target.Name + "</enemy>" + " has " +
                           target.currentHP + "/" + target.maxHP + " HP remaining.");

        if (!target.isAlive)
        {
            PTAdventureLog.Log("<enemy>" + target.Name + "</enemy>" + " has been slain by " + attacker.Name + "!");
            ChangeGold(target.goldReward);
            ChangeXP(target.accumulatedLifeXp, "<enemy>" + target.Name + "</enemy>");
            PTAdventureLog.Log("<enemy>" + target.Name + "</enemy>" + " has been slain! You found " + target.goldReward + " gold on the corpse.");
            Destroy(target.gameObject);
            adversaries.Remove(target);
        }

        return adversaries.Count == 0;
    }


    private void ProcessEnemyAttack(PTSoul enemyAttacker)                                       /// Executes one enemy's automatic attack, processes the result immediately.
    {
        // Filter for party members still in combat (alive and haven't fled)
        List<PTSoul> availableTargets = partyMembers.Where(m => m.isAlive && !m.hasFled).ToList();

        if (availableTargets.Count == 0) return;                     // no one left to target (all fled or dead)

        PTSoul partyTarget = availableTargets[Random.Range(0, availableTargets.Count)];
        int enemyDamage = enemyAttacker.DealDamage();
        bool wasCrit = enemyAttacker.WasCrit;
        int actualDamage = partyTarget.TakeDamage(enemyDamage);
        string critTag = wasCrit ? " <color=yellow>CRITICAL HIT!</color>" : "";

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
            RemovePartyMember(partyTarget);
        }
    }
#endregion

#region Player Input Handlers
    public void OnAttack(InputAction.CallbackContext context)                                            //Method to handle Left Click / Attack key
    {
        if (!context.performed) return;
        if (State != GameState.Combat) return;                                                           //clicks outside of battle do nothing
        if (pointerOverUI && context.control != null && context.control.device is Pointer) return;       //a click on an action button is not an attack
        TryAttack();
    }

    public void TryAttack()                                                                              //the active party member attacks a random enemy
    {
        if (State == GameState.GameOver) return;

        if (adversaries.Count == 0)
        {
            PTAdventureLog.Log("There are no enemies to attack!");
            return;
        }

        if (!awaitingPartyInput)
        {
            PTAdventureLog.Log("It is not the party's turn yet!");
            return;
        }

        // Safety: skip if active actor died since their turn was announced
        if (activeActor == null || !activeActor.isAlive)
        {
            awaitingPartyInput = false;
            AdvancePartyTurn();
            return;
        }

        // Execute this party member's attack
        awaitingPartyInput = false;
        PTSoul attacker = activeActor;
        activeActor = null;

        bool allEnemiesDead = ProcessPartyAttack(attacker);
        UpdateUI();

        if (allEnemiesDead)
        {
            OnAllEnemiesDefeated();
            return;
        }

        // Resume the party phase — advance to the next party member
        AdvancePartyTurn();
    }

    // ─── Victory handler ───
    private void OnAllEnemiesDefeated()
    {
        ResetCombatState();
        ChangeGold(commission);
        SetSunIntensity(0.1f);
        State = GameState.Town;
        PTAdventureLog.Log("You have defeated all the enemies! You can now sleep through the night.");
        PTAdventureLog.Log("You earned a commission of " + commission + " gold! You have " + gold + " gold in total.");
        UpdateUI();
    }

    // ─── Combat ended without victory (all fled or died) ───
    private void OnCombatEndedWithoutVictory()
    {
        // Destroy all remaining enemies
        for (int i = adversaries.Count - 1; i >= 0; i--)
        {
            Destroy(adversaries[i].gameObject);
        }
        adversaries.Clear();

        ResetCombatState();
        SetSunIntensity(0.1f);
        State = GameState.Town;
        PTAdventureLog.Log("All party members have fled or died! No gold was looted and no commission was earned.");
        UpdateUI();
    }

    public void OnRunAway(InputAction.CallbackContext context)                                           //Method to handle R key press - Run from battle
    {
        if (context.performed) TryRunAway();
    }

    public void TryRunAway()                                                                             //the active party member tries to flee the battle
    {
        if (State == GameState.GameOver) return;                                                         //block input after game over

        if (adversaries.Count == 0)                                                                       //if not in combat, can't flee
        {
            PTAdventureLog.Log("There is no battle to run from!");                                          //log no battle message
            return;
        }

        if (!awaitingPartyInput)                                                                          //only allow flee on party member's turn
        {
            PTAdventureLog.Log("You can only attempt to run away on your turn!");
            return;
        }

        // Safety: skip if active actor died or is invalid
        if (activeActor == null || !activeActor.isAlive)
        {
            awaitingPartyInput = false;
            AdvancePartyTurn();
            return;
        }

        // The active party member attempts to flee
        PTSoul runner = activeActor;
        awaitingPartyInput = false;                                                                       //end input wait
        activeActor = null;                                                                               //clear active actor

        // Calculate average enemy agility
        float avgEnemyAgility = 0f;
        foreach (PTSoul enemy in adversaries)
        {
            avgEnemyAgility += enemy.atrAgility;
        }
        avgEnemyAgility /= adversaries.Count;

        int fleeCost = FleeCost;                                                                         //10% of current gold, minimum 1
        ChangeGold(-fleeCost);                                                                             //deduct flee cost
        PTAdventureLog.Log(runner.Name + " attempts to flee! " + fleeCost + " gold was lost.");

        // Base 50% chance, +/- difference between member agility and average enemy agility
        float agilityDifference = runner.atrAgility - avgEnemyAgility;
        int escapeChance = Mathf.Clamp(50 + Mathf.RoundToInt(agilityDifference), 5, 95);                //clamp between 5% and 95%

        int roll = Random.Range(0, 100);
        bool escaped = roll < escapeChance;

        if (escaped)
        {
            runner.hasFled = true;                                                                          // Mark as fled
            runner.gameObject.SetActive(false);                                                             // Hide from scene
            if (!runner.markedByDeath)                                                                      // Mark as cowardly
            {
                runner.isCowardly = true;
            }
            PTAdventureLog.Log(runner.Name + " escaped! (" + escapeChance + "% chance) They have been marked as Cowardly!");
        }
        else
        {
            PTAdventureLog.Log(runner.Name + " failed to run away! (" + escapeChance + "% chance)");
        }

        // Check if no one is left in combat
        if (AreNoPartyMembersInCombat())
        {
            OnCombatEndedWithoutVictory();
        }
        else
        {
            // Continue to next party member's turn
            AdvancePartyTurn();
        }

        UpdateUI();
    }
#endregion
}
