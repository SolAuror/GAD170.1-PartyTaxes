using UnityEngine;
using PartyTaxes;


/// PTManager partial — Actions Part: read-only costs and availability used by the clickable action bar.
/// The actions themselves (TrySleep, TryHeal, TryEncounter, TryAttack...) live in the Town and Combat parts.
public partial class PTManager
{
    public int HealCost => partyMembers.Count * HEAL_COST_PER_MEMBER;                                        //10 gold per party member
    public int BlessCost => partyMembers.Count * BLESS_COST_PER_MEMBER;                                      //100 gold per party member
    public int FleeCost => Mathf.Max(1, Mathf.FloorToInt(gold * FLEE_COST_PERCENT));                        //10% of current gold, minimum 1
    public int TotalDailyWages => CalculateTotalDailyWages();
    public bool PartyFull => partyMembers.Count >= maxPartyCount;
    public bool ResurrectionUnlocked => daysElapsed >= RESURRECTION_UNLOCK_DAY;
    public int FallenCount => deadCharacters.Count;

    public int CheapestResurrectionCost                                                                      //resurrection picks a random fallen member, so show the lowest possible price
    {
        get
        {
            if (deadCharacters.Count == 0) return BASE_RESURRECTION_COST;
            int cheapestWages = int.MaxValue;
            foreach (DeadCharacterData dead in deadCharacters)
            {
                cheapestWages = Mathf.Min(cheapestWages, dead.dailywages);
            }
            return BASE_RESURRECTION_COST + cheapestWages;
        }
    }
}
