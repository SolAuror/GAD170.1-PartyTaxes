using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using PartyTaxes;


/// PTManager partial — Town Part: interactions: sleep, heal, bless, recruit, resurrect, and reset.
public partial class PTManager
{
    bool CanPerformTownAction(string combatMessage = "You must return to town first!")                   //helper to check common town action preconditions
    {
        if (State == GameState.GameOver) return false;                                                   //block if game over
        if (State == GameState.Combat)                                                                   //block if in combat
        {
            PTAdventureLog.Log(combatMessage);
            return false;
        }
        return true;
    }

    public void OnSleep(InputAction.CallbackContext context)                                             //Method to handle Spacebar press, using new InputActions system
    {
        if (context.performed) TrySleep();
    }

    public void TrySleep()                                                                               //Sleep through the night: pay wages, rest, roll for theft and start a new day
    {
        if (State == GameState.GameOver) return;                                                         //block input after game over
        if (State == GameState.Combat)                                                                   //no sleeping in the middle of a battle
        {
            PTAdventureLog.Log("You cannot sleep during a battle!");                                     //log cannot sleep message
            return;
        }

        daysElapsed++;                                                                                   //increment the day
        PTAdventureLog.Log(sleepMsgs[Random.Range(0, sleepMsgs.Length)] + " Day: " + daysElapsed);       //log sleeping message
        totalWages = CalculateTotalDailyWages();                                                         //calculate total daily wages from all party members

        bool wagesPaid = gold >= totalWages;                                                             //check if the party can afford to pay wages
        if (wagesPaid)
        {
            ChangeGold(-totalWages);                                                                     //subtract total daily wages from gold
            daysWithoutGold = 0;                                                                         //reset broke counter since wages were paid
        }
        else                                                                                             //if the party cannot afford wages
        {
            daysWithoutGold++;                                                                           //increment the broke day counter
            int daysRemaining = DAYS_UNTIL_MUTINY - daysWithoutGold;                                     //calculate days remaining before game over
            if (daysWithoutGold >= DAYS_UNTIL_MUTINY)                                                    //game over after 3 consecutive days without gold
            {
                TriggerGameOver("The party has gone unpaid for 3 days and murdered you in your sleep!");
                return;                                                                                  //exit early
            }
            PTAdventureLog.Log("WARNING: The party cannot be paid! They are very unhappy about this. " + daysRemaining + " day(s) until mutiny!"); //log warning message
        }

        RestParty(wagesPaid);                                                                            //paid members rest and recover health, unpaid ones don't

        // Theft chance — higher odds and larger cut during the broke period
        float theftChance  = daysWithoutGold > 0 ? THEFT_CHANCE_BROKE : THEFT_CHANCE_NORMAL;             //25% chance for party theft when broke, 10% otherwise
        float theftPercent = daysWithoutGold > 0 ? THEFT_PERCENT_BROKE : THEFT_PERCENT_NORMAL;           //they steal 20% when you are broke, 10% otherwise
        if (Random.value < theftChance && gold > 0)                                                      //roll for theft, only if there is gold to steal
        {
            int stolen = Mathf.Max(1, Mathf.FloorToInt(gold * theftPercent));                            //calculate amount stolen, minimum 1
            ChangeGold(-stolen);                                                                         //deduct stolen gold
            string theftMsg = daysWithoutGold > 0
                ? "The party is getting desperate... " + stolen + " gold has gone missing!"              //broke period message
                : "The party has stolen from you. " + stolen + " gold is missing!";                      //normal theft message
            PTAdventureLog.Log(theftMsg);                                                                //log theft message
        }

        if (daysElapsed % WEEKLY_BONUS_INTERVAL == 0)                                                    //every seventh day, award the weekly bonus commission
        {
            ChangeGold(weeklyBonusCommission);                                                           //add weekly bonus commission
            PTAdventureLog.Log("A week has passed! The party earns a weekly bonus of " +
                               weeklyBonusCommission + " gold!");                                        //log weekly bonus message
        }

        if (daysElapsed == RESURRECTION_UNLOCK_DAY)                                                      //on the 7th day, unlock resurrection
        {
            PTAdventureLog.Log("<color=purple>You feel an ominous presence in town, the souls of the dead are restless...</color>");
        }

        SetSunIntensity(2f);
        hasFought = false;                                                                               //reset hasFought for the new day
        hasHealed = false;                                                                               //reset hasHealed for the new day
        hasBeenBlessed = false;                                                                          //reset hasBeenBlessed for the new day
        UnhideAllFledMembers();                                                                          //unhide any party members who fled during combat

        dailyMsg = "Heil, " + partyName +                                                                //greet the party,
                  "! You have " + gold +                                                                 //display the party gold
                  " gold and " + partyMembers.Count +                                                    //display the number of party members
                  " party members. Daily Wages are " + totalWages +                                      //display the total daily wages 
                  ". Your party may Heal if the cleric is available, look for new members or head out on a hunt." + //display instructions for fighting enemies
                  " You may also sleep through this night.";                                             //display instructions for sleeping
        PTAdventureLog.Log(dailyMsg);                                                                    //log the daily message
        UpdateUI();                                                                                      //update UI after sleeping

        if (debugMode)
        {
            Debug.Log("party slept");
        }
    }

    void RestParty(bool wagesPaid)                                                                       //overnight recovery: a paid party rests and recovers a share of max HP
    {
        if (!wagesPaid)
        {
            PTAdventureLog.Log("Unpaid and resentful, the party sleeps poorly and recovers no health.");
            return;
        }

        int totalRested = 0;
        foreach (PTSoul member in partyMembers)
        {
            totalRested += member.RestoreHP(Mathf.RoundToInt(member.maxHP * REST_HEAL_PERCENT));
        }
        if (totalRested > 0)
        {
            PTAdventureLog.Log("The party rests through the night and recovers " + totalRested + " HP.");
        }
    }

    public void OnHeal(InputAction.CallbackContext context)                                              //method to handle Q key press aka Healing
    {
        if (context.performed) TryHeal();
    }

    public void TryHeal()
    {
        if (!CanPerformTownAction("You cannot heal during battle!")) return;

        
        if (hasHealed)                                                                                      //if already healed today
        {
          PTAdventureLog.Log("You have already exhausted the local cleric, try again tomorrow.");
          return;
        }

        int healCost = HealCost;                                                                         // Calculate cost (10 gold per party member)

        if (gold < healCost)                                                                                //if the player cant afford heals
        {
          PTAdventureLog.Log("Not enough gold! You need " + healCost + 
                             " gold to heal the party, but you only have " + gold + " gold.");
          return;
        }

        ChangeGold(-healCost);
        int totalHealed = 0;
        foreach (PTSoul member in partyMembers)
        {
          int healed = member.Heal(BASE_HEAL_AMOUNT);
          totalHealed += healed;

          PTAdventureLog.Log(member.Name + " was healed for " + healed + " HP! (" + member.currentHP + "/" + member.maxHP + " HP)");
        }

        hasHealed = true;                                                                                   //set healing as used for the day


        PTAdventureLog.Log("The Party has been Healed! Healing received: " + totalHealed + " HP. Cost: " + healCost + " gold. Remaining gold: " + gold);
    }

    public void OnRecruit(InputAction.CallbackContext context)                                           //Method to handle F key to recruit a new party member
    {
        if (context.performed) TryRecruit();
    }

    public void TryRecruit()
    {
        if (!CanPerformTownAction("You are in Combat! Win the battle before recruiting new members!")) return;

        if (partyMembers.Count >= maxPartyCount)                                                            //if the party is full, prevent recruitment
        {
          PTAdventureLog.Log("The party is full! You cannot recruit more members right now.");              //log party full message
          return;
        }

        if (partyBasePrefab == null || soulGen == null)                                                     //if base prefab or soul generator is not assigned
        {
          PTAdventureLog.Log("No one is available to join the party!");                                     //log message, no one to recruit
          return;
        }

        int recruitCost = GetRecruitCost(out bool hasCoward);                                            //recruitment cost is half of total wages, x1.5 with cowards in the party
        if (hasCoward)
        {
          PTAdventureLog.Log("Cowardly members in the party have increased recruitment costs!");         //log cowardly surcharge
        }

        if (gold < recruitCost)                                                                              //check if party can afford recruitment
        {
          PTAdventureLog.Log("Not enough gold to recruit! You need " + recruitCost +
                             " gold but only have " + gold + " gold.");                                    //log insufficient gold
          return;
        }

        ChangeGold(-recruitCost);                                                                            //deduct recruitment cost
        PTAdventureLog.Log("Recruitment cost: " + recruitCost + " gold. Remaining: " + gold + " gold."); //log recruitment cost

        SpawnRandomPartyMember();                                                                           //spawn a random soul using the base prefab and soul generator
        
        UpdateUI();                                                                                         //update UI after recruitment (gold changed, possibly party count changed)
    }

    public void OnReset(InputAction.CallbackContext context)                                             //Method to handle Escape key press - reloads the active scene
    {
        if (context.performed) RestartRun();
    }

    public void RestartRun()                                                                             //reloads the active scene, resetting all state
    {
        PTAdventureLog.ClearLog();
        PTAdventureLog.Log("Restarting...");                                                             //log reset message
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);                                //reload the current scene, resetting all state
    }

    public void OnBless(InputAction.CallbackContext context)                                             //for blessing the Party
    {
        if (context.performed) TryBless();
    }

    public void TryBless()
    {
        if (!CanPerformTownAction("You are not near the cleric, Go back to Town!")) return;

        
            if (hasBeenBlessed)                                                                                      //if already healed today
            {
                PTAdventureLog.Log("You have already received the blessings of the cleric this day!");
                return;
            }

            int blessCost = BlessCost;                                                                   // Calculate cost (100 gold per party member)

            if (gold < blessCost)                                                                                //if the player cant afford blessings
            {
                PTAdventureLog.Log("The Cleric scoffs at your wealth! Come back with " + blessCost + 
                             " gold to bless the party!!");
                return;
            }

            ChangeGold(-blessCost);
            foreach (PTSoul member in partyMembers)
            {
            member.Bless();
            }

            hasBeenBlessed = true;                                                                              //set blessed as used for the day


            PTAdventureLog.Log("The Party has been Blessed! Cost: " + blessCost + " gold. Remaining gold: " + gold);
    }

    public void OnResurrect(InputAction.CallbackContext context)                                         //resurrect a fallen party member
    {
        if (context.performed) TryResurrect();
    }

    public void TryResurrect()
    {
        if (!CanPerformTownAction("You are not with the Cleric, the souls of the dead will tear you asunder!")) return;

            if (daysElapsed < RESURRECTION_UNLOCK_DAY)                                                      //resurrection only available after day 7
            {
                PTAdventureLog.Log("The veil between life and death is still too strong...");
                return;
            }

            if (partyMembers.Count >= maxPartyCount)                                                        //if party is full
            {
                PTAdventureLog.Log("The party is full! Make room before attempting Resurrection.");
                return;
            }

            if (deadCharacters.Count == 0)                                                                  //check if there are any dead characters to resurrect
            {
                PTAdventureLog.Log("There are no souls available for Resurrection.");
                return;
            }

            //select random dead character
            DeadCharacterData deadChar = deadCharacters[Random.Range(0, deadCharacters.Count)];
            int resurrectionCost = BASE_RESURRECTION_COST + deadChar.dailywages;                            //cost is 100 + character's daily wages

            if (gold < resurrectionCost)                                                                    //check if player can afford resurrection
            {
                PTAdventureLog.Log("Resurrection requires " + resurrectionCost + " gold. You only have " + gold + " gold.");
                return;
            }

            ChangeGold(-resurrectionCost);                                                                  //deduct resurrection cost
            PTAdventureLog.Log("The dark ritual begins... " + resurrectionCost + " gold consumed.");

            Quaternion resurrectionRotation;
            Vector3 spawnPos = GetNextPartyMemberTransform(out resurrectionRotation);                      //reposition existing members and get next slot
            GameObject resurrectedObj = Instantiate(deadChar.originalPrefab, spawnPos, resurrectionRotation, transform);
            PTSoul resurrectedSoul = resurrectedObj.GetComponent<PTSoul>();

            if (resurrectedSoul != null)
            {
                deadChar.ApplyTo(resurrectedSoul);                                                          //restore character data with penalties via centralized method

                partyMembers.Add(resurrectedSoul);

                //remove from dead lists
                deadCharacters.Remove(deadChar);

                PTAdventureLog.Log(deadChar.name + " has been Resurrected! They are Marked by Death (-10% stats, Fearless).");
                
                if (debugMode)
                {
                    Debug.Log("Resurrected: " + deadChar.name);
                }
            }

            UpdateUI();
    }
}
