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
        if (IsRunOver) return false;                                                                     //block if the run is over
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

    public void TrySleep()                                                                               //End the day: the Queen's tribute, payroll, desertions, mutiny, rest and theft, then a new morning
    {
        if (IsRunOver) return;                                                                           //block input after the run ends
        if (State == GameState.Combat)                                                                   //no sleeping in the middle of a battle
        {
            PTAdventureLog.Log("You cannot sleep during a battle!");                                     //log cannot sleep message
            return;
        }
        if (PendingConfrontation != null)                                                                //a caught thief needs a decision first
        {
            PTAdventureLog.Log("Decide what to do about " + PendingConfrontation.Name + " before you turn in.");
            return;
        }

        PTAdventureLog.Log(sleepMsgs[Random.Range(0, sleepMsgs.Length)]);                                //log sleeping message

        if (IsTributeDay && !CollectTribute()) return;                                                   //the Queen gets paid first; stop here if the run ended

        HashSet<PTSoul> paid = RunPayroll();                                                             //pay everyone ticked for payroll while the gold lasts
        ResolveDesertions();                                                                             //members with no loyalty left walk out
        if (partyMembers.Count == 0)                                                                     //nobody left to protect you
        {
            TriggerGameOver("With no one left to protect you, the goblins find you before the Queen does.", "Abandoned");
            return;
        }
        if (CheckMutiny()) return;                                                                       //game over if the party turns on you

        RestParty(paid);                                                                                 //paid members rest and recover health, unpaid ones don't
        int expectedGold = gold;
        RunNightlyTheft();                                                                               //sticky fingers in the night

        daysElapsed++;                                                                                   //increment the day
        PTAdventureLog.Log("Day " + daysElapsed + " dawns.");
        LogMorningLedger(expectedGold);

        if (daysElapsed == RESURRECTION_UNLOCK_DAY)                                                      //on the 7th day, unlock resurrection
        {
            PTAdventureLog.Log("<color=purple>You feel an ominous presence in town, the souls of the dead are restless...</color>");
        }

        SetSunIntensity(2f);
        hasFought = false;                                                                               //reset hasFought for the new day
        hasHealed = false;                                                                               //reset hasHealed for the new day
        hasBeenBlessed = false;                                                                          //reset hasBeenBlessed for the new day
        hasAudited = false;                                                                              //one audit per day
        UnhideAllFledMembers();                                                                          //unhide any party members who fled during combat
        RefreshTavern();                                                                                 //new faces at the tavern

        dailyMsg = "Heil, " + partyName +                                                                //greet the party,
                  "! You have " + gold +                                                                 //display the party gold
                  " gold and " + partyMembers.Count +                                                    //display the number of party members
                  " party members. Daily Wages are " + CalculateTotalDailyWages() + ". " +               //display the total daily wages
                  (IsTributeDay ? "The Queen's auditor is in town: " + NextTributeAmount + " gold is due TONIGHT." //tribute reminder
                                : NextTributeAmount + " gold is due to the Queen in " + DaysUntilTribute + " day(s).");
        PTAdventureLog.Log(dailyMsg);                                                                    //log the daily message
        UpdateUI();                                                                                      //update UI after sleeping

        if (debugMode)
        {
            Debug.Log("party slept");
        }
    }

    void RestParty(HashSet<PTSoul> paidMembers)                                                          //overnight recovery: paid members rest and recover a share of max HP
    {
        int totalRested = 0;
        foreach (PTSoul member in partyMembers)
        {
            if (!paidMembers.Contains(member)) continue;                                                 //unpaid and resentful, they sleep poorly
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

    public void OnRecruit(InputAction.CallbackContext context)                                           //Method to handle F key: open the tavern's hiring board
    {
        if (!context.performed) return;
        if (actionBar != null)
        {
            actionBar.ToggleTavern();                                                                    //pick who to hire with the mouse
            return;
        }
        PTSoul cheapest = CheapestAffordableCandidate();                                                 //no action bar: hire the cheapest affordable candidate
        if (cheapest != null)
        {
            TryHire(tavernCandidates.IndexOf(cheapest));
        }
        else
        {
            PTAdventureLog.Log("No one at the tavern will sign on for what you can pay.");
        }
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
                ChangeLoyalty(member, LOYALTY_BLESSED);                                                  //a blessing on your coin earns some goodwill
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
                deadChar.ApplyTo(resurrectedSoul);                                                       //restore character data with penalties via centralized method
                partyMembers.Add(resurrectedSoul);
                Employ(resurrectedSoul, LOYALTY_RESURRECTED);                                            //back on the payroll, and grateful for it

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
