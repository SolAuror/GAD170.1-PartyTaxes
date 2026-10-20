using System.Collections.Generic;
using UnityEngine;
using PartyTaxes;

/// PTManager partial — Party Part: formation, member management, and leveling helpers.
public partial class PTManager
{
    Vector3 GetNextPartyMemberTransform(out Quaternion rotation)                                         //Helper: repositions existing members and returns position + rotation for the next member
    {
        rotation = Quaternion.Euler(0, 180, 0);                                                             //set correct rotation for party members (facing positive Z)
        
        Vector3 baseSpawnPosition = partySpawnPoint != null ?
                                    partySpawnPoint.position :
                                    transform.position;                                                     //use spawn point if assigned, otherwise use PTManager position
        float spacing = 1.2f;                                                                               //spacing between party members along x-axis
        int newCount = partyMembers.Count + 1;                                                              //total party size after this member is added
        float totalWidth = (newCount - 1) * spacing;                                                        //total width of the party formation
        float startX = -(totalWidth / 2f);                                                                  //leftmost position, so the group stays centered on the spawn point

        for (int j = 0; j < partyMembers.Count; j++)                                                       //reposition existing members to keep the group centered
        {
            partyMembers[j].transform.position = baseSpawnPosition + new Vector3(startX + j * spacing, 0, 0);
            partyMembers[j].transform.rotation = rotation;                                                 //apply party member rotation
        }

        return baseSpawnPosition + new Vector3(startX + partyMembers.Count * spacing, 0, 0);               //new member takes the last slot
    }

    void SpawnRandomPartyMember()                                                                          //Method to spawn a random party member using soulGen and the base prefab
    {
        if (partyMembers.Count >= maxPartyCount)                                                            //check if the party is not full
        {
            PTAdventureLog.Log("The party is full! You cannot add more members to the party.");
            return;
        }

        if (partyBasePrefab == null || soulGen == null)                                                     //check base prefab and soul generator are assigned
        {
            if (debugMode) Debug.Log("Cannot spawn party member: partyBasePrefab or soulGen is missing.");
            return;
        }

        Quaternion spawnRotation;
        Vector3 spawnPosition = GetNextPartyMemberTransform(out spawnRotation);                             //reposition existing members and get the next slot in the formation

        GameObject newRecruit = soulGen.CreateRandomRecruit(partyBasePrefab, transform, spawnPosition);     //instantiate and randomize via soul generator
        if (newRecruit != null)
        {
            newRecruit.transform.rotation = spawnRotation;                                                  //apply party member rotation
            PTSoul soul = newRecruit.GetComponent<PTSoul>();
            if (soul != null)
            {
                partyMembers.Add(soul);                                                                    //add the new member to the party
                LevelUpToPartyAverage(soul);                                                                //level up new recruit to match party average
                PTAdventureLog.Log(soul.Name + " joined " + partyName + "!");                               //log message that a new member has joined
            }
        }
    }

    void RemovePartyMember(PTSoul member)                                                                   //Method to remove a party member and destroy their GameObject
    {
        if (partyMembers.Contains(member))
        {
            //only add to dead lists if the member is actually dead
            if (!member.isAlive)
            {
                deadCharacters.Add(new DeadCharacterData(member, partyBasePrefab));                             //store character data with base prefab for resurrection
            }
            
            partyMembers.Remove(member);                                                                    //remove from list
            Destroy(member.gameObject);                                                                     //destroy the GameObject

            PTAdventureLog.Log(member.Name + " has left the party!");                                       //log message that a member has left

            if (debugMode)                                                                                  //if debug mode is enabled, print debug message
            {
                Debug.Log(member.Name + " has left the party!");
            }
        }
    }

    void LevelUpToPartyAverage(PTSoul newMember)                                                            //Level up new member to match party average
    {
        if (partyMembers.Count <= 1) return;                                                                //no need if they're the only member or first member

        int totalLevels = 0;
        foreach (PTSoul member in partyMembers)
        {
            if (member != newMember)                                                                        //exclude the new member from calculation
            {
                totalLevels += member.level;
            }
        }
        int avgLevel = totalLevels / (partyMembers.Count - 1);

        while (newMember.level < avgLevel)
        {
            newMember.LevelUp();
        }
        newMember.currentHP = newMember.maxHP;                                                           //new recruits always join at full health
        newMember.UpdateUI();

        if (newMember.level > 1 && debugMode)
        {
            Debug.Log(newMember.Name + " leveled up to match party average (Level " + avgLevel + ")");
        }
    }

    int GetAveragePartyLevel()
    {
        if (partyMembers.Count == 0) return 0;
        int total = 0;
        foreach (PTSoul member in partyMembers)
        {
            total += member.level;
        }
        return total / partyMembers.Count;
    }

    /// Debug method: grants every party member the given amount of XP, to test level ups.
    /// Only works when debugMode is enabled.
    public void DebugGrantPartyXP(int amountPerMember)
    {
        if (!debugMode)
        {
            Debug.LogWarning("Debug mode is not enabled. Cannot grant XP.");
            return;
        }
        ChangeXP(amountPerMember * partyMembers.Count, "[DEBUG]");                                       //ChangeXP splits the pool evenly between members
        UpdateUI();
    }
}
