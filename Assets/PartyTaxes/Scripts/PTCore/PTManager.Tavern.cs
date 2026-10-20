using System.Collections.Generic;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Tavern Part: the hiring board. Three visible candidates every morning,
/// each with their stats, wage and a hint about their greed. Candidates are real souls kept inactive until hired.
public partial class PTManager
{
    private const int TAVERN_CANDIDATES = 3;                                                                 //candidates on the board each day
    private const int SIGNING_FEE_DAYS = 2;                                                                  //signing fee = this many days of the candidate's wage
    private const float COWARD_FEE_MULTIPLIER = 1.5f;                                                        //word gets around: cowards in the party make hiring pricier

    private readonly List<PTSoul> tavernCandidates = new List<PTSoul>();
    public IReadOnlyList<PTSoul> TavernCandidates => tavernCandidates;
    public int TavernSize => TAVERN_CANDIDATES;

    void RefreshTavern()                                                                                     //replace the board with fresh candidates
    {
        foreach (PTSoul candidate in tavernCandidates)
        {
            if (candidate != null) Destroy(candidate.gameObject);
        }
        tavernCandidates.Clear();
        if (partyBasePrefab == null || soulGen == null) return;

        int partyLevel = GetAveragePartyLevel();
        for (int i = 0; i < TAVERN_CANDIDATES; i++)
        {
            GameObject candidateObj = soulGen.CreateRandomRecruit(partyBasePrefab, transform, transform.position);
            if (candidateObj == null) continue;
            candidateObj.SetActive(false);                                                                   //waits off-stage until hired
            PTSoul candidate = candidateObj.GetComponent<PTSoul>();
            if (candidate == null) continue;
            GiveTraits(candidate, RollTraits());                                                         //every candidate has a trait or two
            LevelUpTo(candidate, partyLevel + TraitLevelBonus(candidate), false);                        //candidates arrive at the party's level (Veterans above it)
            tavernCandidates.Add(candidate);
        }
    }

    private const int LOOT_SHARE_FEE_DAYS = 1;                                                           //loot share: small fee, no wage, a cut of every job
    private const float LOOT_SHARE_PERCENT = 0.16f;
    private const int SIGNING_BONUS_FEE_DAYS = 5;                                                        //signing bonus: big fee up front...
    private const float SIGNING_BONUS_WAGE = 0.6f;                                                       //...then 60% of the wage, for good

    public float LootSharePercent => LOOT_SHARE_PERCENT;

    public int SigningFee(PTSoul candidate, ContractType contract = ContractType.DayWage)                //what it costs to sign someone on each contract
    {
        if (candidate == null) return 0;
        int days = contract == ContractType.LootShare ? LOOT_SHARE_FEE_DAYS : contract == ContractType.SigningBonus ? SIGNING_BONUS_FEE_DAYS : SIGNING_FEE_DAYS;
        float fee = candidate.dailywages * days * FeeMultiplier;
        if (partyMembers.Exists(member => member.isCowardly))
        {
            fee *= COWARD_FEE_MULTIPLIER;                                                                //word gets around
        }
        return Mathf.RoundToInt(fee);
    }

    public int ContractWage(PTSoul candidate, ContractType contract) =>                                  //the daily wage they'd draw on each contract
        candidate == null ? 0 : contract == ContractType.LootShare ? 0 :
        contract == ContractType.SigningBonus ? candidate.WageFor(Mathf.Max(1, Mathf.RoundToInt(candidate.baseWage * SIGNING_BONUS_WAGE))) :
        candidate.dailywages;

    public void TryHire(int index, ContractType contract = ContractType.DayWage)                         //hire a candidate from the tavern board
    {
        if (!CanPerformTownAction("You are in Combat! Win the battle before hiring anyone!")) return;
        if (index < 0 || index >= tavernCandidates.Count) return;
        if (partyMembers.Count >= maxPartyCount)
        {
            PTAdventureLog.Log("The party is full! Fire someone first if you want to make room.");
            return;
        }

        PTSoul candidate = tavernCandidates[index];
        int fee = SigningFee(candidate, contract);
        if (gold < fee)
        {
            PTAdventureLog.Log(candidate.Name + " wants " + fee + " gold to sign on, but you only have " + gold + " gold.");
            return;
        }

        ChangeGold(-fee);
        tavernCandidates.RemoveAt(index);
        LevelUpTo(candidate, GetAveragePartyLevel() + TraitLevelBonus(candidate), false);                //in case the party levelled since the morning
        candidate.currentHP = candidate.maxHP;
        Quaternion rotation;
        Vector3 position = GetNextPartyMemberTransform(out rotation);
        candidate.transform.position = position;
        candidate.transform.rotation = rotation;
        candidate.gameObject.SetActive(true);
        partyMembers.Add(candidate);
        Employ(candidate);
        candidate.employment.contract = contract;
        if (contract == ContractType.SigningBonus)
        {
            candidate.baseWage = Mathf.Max(1, Mathf.RoundToInt(candidate.baseWage * SIGNING_BONUS_WAGE));
        }
        candidate.RecalculateWage();                                                                     //loot share draws no wage
        candidate.UpdateUI();
        string terms = contract == ContractType.LootShare ? "a " + Mathf.RoundToInt(LOOT_SHARE_PERCENT * 100) + "% cut of every job"
                     : candidate.dailywages + " gold a day" + (contract == ContractType.SigningBonus ? " (signing bonus deal)" : "");
        PTAdventureLog.Log(candidate.Name + " joined " + partyName + "! Signing fee " + fee + " gold, for " + terms + ".");
        if (candidate.employment.traits.Count > 0)
        {
            foreach (string id in candidate.employment.traits)
            {
                PTTrait trait = PTContent.Trait(id);
                if (trait != null) PTAdventureLog.Log(candidate.Name + " is " + trait.name + ": " + trait.description);
            }
        }
        UpdateUI();
    }

    PTSoul CheapestAffordableCandidate()                                                                     //used by the F key when there's no action bar
    {
        PTSoul best = null;
        foreach (PTSoul candidate in tavernCandidates)
        {
            if (SigningFee(candidate) <= gold && (best == null || candidate.dailywages < best.dailywages)) best = candidate;
        }
        return best;
    }
}
