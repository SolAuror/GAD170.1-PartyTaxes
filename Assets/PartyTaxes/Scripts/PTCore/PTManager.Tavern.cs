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
            LevelUpTo(candidate, partyLevel, false);                                                         //candidates arrive at the party's level
            CreateEmploymentRecord(candidate);
            tavernCandidates.Add(candidate);
        }
    }

    public int SigningFee(PTSoul candidate)
    {
        if (candidate == null) return 0;
        float fee = candidate.dailywages * SIGNING_FEE_DAYS;
        if (partyMembers.Exists(member => member.isCowardly))
        {
            fee *= COWARD_FEE_MULTIPLIER;
        }
        return Mathf.RoundToInt(fee);
    }

    public void TryHire(int index)                                                                           //hire a candidate from the tavern board
    {
        if (!CanPerformTownAction("You are in Combat! Win the battle before hiring anyone!")) return;
        if (index < 0 || index >= tavernCandidates.Count) return;
        if (partyMembers.Count >= maxPartyCount)
        {
            PTAdventureLog.Log("The party is full! Fire someone first if you want to make room.");
            return;
        }

        PTSoul candidate = tavernCandidates[index];
        int fee = SigningFee(candidate);
        if (gold < fee)
        {
            PTAdventureLog.Log(candidate.Name + " wants " + fee + " gold to sign on, but you only have " + gold + " gold.");
            return;
        }

        ChangeGold(-fee);
        tavernCandidates.RemoveAt(index);
        LevelUpTo(candidate, GetAveragePartyLevel(), false);                                                 //in case the party levelled since the morning
        candidate.currentHP = candidate.maxHP;
        Quaternion rotation;
        Vector3 position = GetNextPartyMemberTransform(out rotation);
        candidate.transform.position = position;
        candidate.transform.rotation = rotation;
        candidate.gameObject.SetActive(true);
        partyMembers.Add(candidate);
        Employ(candidate);
        candidate.UpdateUI();
        PTAdventureLog.Log(candidate.Name + " joined " + partyName + "! Signing fee " + fee + " gold, wages " + candidate.dailywages + " gold a day.");
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
