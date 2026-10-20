using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Ledger Part: employment, payroll, loyalty, theft, audits, firing, desertion and mutiny.
/// Tuning values come from Tools/BalanceSim; keep them in sync.
public partial class PTManager
{
    public enum Confrontation { DockPay, Forgive, Fire }

    private const int LOYALTY_START = 60;                                                                    //loyalty of a fresh hire
    private const int LOYALTY_RESURRECTED = 80;                                                              //grateful to be alive
    private const int LOYALTY_PAID = 5;                                                                      //paid on time
    private const int LOYALTY_UNPAID = -15;                                                                  //went unpaid tonight
    private const int LOYALTY_ALLY_DIED = -10;                                                               //a party member died in combat
    private const int LOYALTY_KILL = 3;                                                                      //landed a killing blow
    private const int LOYALTY_BLESSED = 10;                                                                  //blessed by the cleric
    private const int LOYALTY_NO_SEVERANCE = -10;                                                            //watched someone get fired without severance
    private const int LOYALTY_WRONGLY_ACCUSED = -15;                                                         //audited while innocent
    private const int LOYALTY_DOCKED = -10;                                                                  //caught stealing and had their pay docked
    private const int LOYALTY_FORGIVEN = 15;                                                             //caught stealing and forgiven
    private const int LOYALTY_SHARE_NO_TAKE = -5;                                                        //loot share: no cut today (rest day, retreat)
    private const float GREED_MIN = 0.1f;
    private const float GREED_MAX = 0.9f;
    private const float THEFT_SCALE = 0.7f;                                                                  //nightly theft chance = greed x (1 - loyalty/150) x this
    private const float THEFT_LOYALTY_DIVISOR = 150f;                                                        //150 rather than 100, so even devoted staff skim a little
    private const float THEFT_CUT_MIN = 0.05f;                                                               //a thief takes 5-12% of your gold
    private const float THEFT_CUT_MAX = 0.12f;
    private const float DESERTION_CUT = 0.1f;                                                                //deserters leave with 10% of your gold
    private const int MUTINY_AVERAGE_LOYALTY = 20;                                                           //mutiny if average loyalty falls below this...
    private const int MUTINY_LOW_LOYALTY = 10;                                                               //...or two or more members fall below this
    private const int MUTINY_WARNING_LOYALTY = 35;                                                           //average loyalty that starts the grumbling
    private const float AUDIT_BASE_CHANCE = 0.8f;                                                            //chance to catch a thief with 3 Sense
    private const float AUDIT_CHANCE_PER_SENSE = 0.05f;                                                      //each point of Sense above 3 hides it better
    private const float AUDIT_MIN_CHANCE = 0.4f;
    private const float AUDIT_MAX_CHANCE = 0.9f;

    public readonly List<PTEmployeeRecord> runLedger = new List<PTEmployeeRecord>();                       //everyone hired this run, for the final ledger
    public bool hasAudited = false;                                                                          //one audit per day
    public PTSoul PendingConfrontation { get; private set; }                                                 //a caught thief waiting for your decision
    public int LastNightMissing { get; private set; }                                                    //gold that vanished overnight
    public PTSoul LastNightThief { get; private set; }                                                   //who took the most (only the Bookkeeper gets a clue)
    public int UnpaidLastNight { get; private set; }                                                         //members who went unpaid last night
    public int TotalWagesPaid { get; private set; }
    public int TotalStolen { get; private set; }
    public int TotalRecovered { get; private set; }
    public int TotalSeverancePaid { get; private set; }

#region Employment
    PTEmployeeRecord CreateEmploymentRecord(PTSoul soul)                                                     //give a soul an employment file (candidates get one so their greed can be hinted)
    {
        if (soul.employment == null)
        {
            soul.employment = new PTEmployeeRecord { greed = Random.Range(GREED_MIN, GREED_MAX) };
        }
        soul.employment.name = soul.Name;
        return soul.employment;
    }

    void Employ(PTSoul soul, int loyalty = -1)                                                           //put a soul on the payroll and in the run ledger (-1 = the run's starting loyalty + traits)
    {
        PTEmployeeRecord job = CreateEmploymentRecord(soul);
        job.status = EmploymentStatus.Employed;
        job.fate = "";
        job.loyalty = loyalty >= 0
            ? loyalty
            : Mathf.Clamp(PTRunRules.StartingLoyalty(CurrentAccusation, LOYALTY_START) + Mathf.RoundToInt(job.TraitSum(t => t.startLoyalty)), 0, 100);
        job.nightsUnpaid = 0;
        job.payTonight = true;
        if (!runLedger.Contains(job))
        {
            job.dayHired = Mathf.Max(1, daysElapsed);                                                       //the starting party signs on before day 1 begins
            runLedger.Add(job);
        }
    }

    void ChangeLoyalty(PTSoul member, int amount)
    {
        if (member != null && member.employment != null)
        {
            member.employment.ChangeLoyalty(amount);
        }
    }

    void ChangePartyLoyalty(int amount, PTSoul except = null)                                                //loyalty change for everyone (optionally except one member)
    {
        foreach (PTSoul member in partyMembers)
        {
            if (member != except) ChangeLoyalty(member, amount);
        }
    }

    public float AverageLoyalty => partyMembers.Count == 0 ? 0f : (float)partyMembers.Average(m => m.employment != null ? m.employment.loyalty : 0);
#endregion

#region Night: payroll, desertion, mutiny, theft
    HashSet<PTSoul> RunPayroll()                                                                         //pay everyone ticked for payroll while the gold lasts; returns who was paid (loot-share members count if they got a cut today)
    {
        HashSet<PTSoul> paid = new HashSet<PTSoul>();
        List<string> unpaidNames = new List<string>();
        int available = gold;
        int totalPaid = 0;

        foreach (PTSoul member in partyMembers)
        {
            PTEmployeeRecord job = member.employment;
            if (job == null) continue;
            job.hungover = false;                                                                        //yesterday's hangover has worn off
            if (job.HasSpecial("gambler") && !job.OnLootShare)                                           //Gambler: tonight's wage is a roll of the dice
            {
                member.dailywages = Mathf.Max(1, Mathf.RoundToInt(member.WageForLevel() * Random.Range(0.5f, 1.5f)));
                job.requestedWage = 0;                                                                       //a gambler's wage already follows their level
            }
            if (job.OnLootShare)                                                                         //loot share: no wage, happy if they got a cut today
            {
                job.ChangeLoyalty(job.tookCutToday ? LOYALTY_PAID + Mathf.RoundToInt(job.TraitSum(t => t.paidLoyalty)) : LOYALTY_SHARE_NO_TAKE);
                job.nightsUnpaid = 0;
                paid.Add(member);
            }
            else if (job.payTonight && available >= member.dailywages)
            {
                available -= member.dailywages;
                totalPaid += member.dailywages;
                job.wagesReceived += member.dailywages;
                job.nightsUnpaid = 0;
                job.ChangeLoyalty(LOYALTY_PAID + Mathf.RoundToInt(job.TraitSum(t => t.paidLoyalty)));
                paid.Add(member);
                if (job.HasSpecial("drunkard") && Random.value < HUNGOVER_CHANCE)                        //Drunkard: payday, then the tavern
                {
                    job.hungover = true;
                    PTAdventureLog.Log(member.Name + " spends their wages at the tavern. They'll be hungover tomorrow.");
                }
            }
            else
            {
                job.nightsUnpaid++;
                job.ChangeLoyalty(Mathf.RoundToInt(LOYALTY_UNPAID * (1f + job.TraitSum(t => t.unpaidLoyaltyPercent) / 100f)));
                unpaidNames.Add(member.Name + (job.payTonight ? "" : " (withheld)"));
                if (job.HasSpecial("union_rep"))                                                         //Union Rep: stiff them and everyone hears about it
                {
                    ChangePartyLoyalty(UNION_REP_EXTRA_LOYALTY, member);
                    PTAdventureLog.Log(member.Name + " tells the whole party they went unpaid. Everyone else loses loyalty too.");
                }
            }
        }

        ChangeGold(-totalPaid);
        TotalWagesPaid += totalPaid;
        UnpaidLastNight = unpaidNames.Count;
        if (unpaidNames.Count == 0)
        {
            PTAdventureLog.Log("Payroll: everyone was paid, " + totalPaid + " gold in wages.");
        }
        else
        {
            PTAdventureLog.Log("Payroll: paid " + paid.Count + " of " + partyMembers.Count + " (" + totalPaid + " gold). Unpaid: " +
                               string.Join(", ", unpaidNames) + ". They won't forget it.");
        }
        return paid;
    }

    void ResolveDesertions()                                                                                 //anyone with no loyalty left walks out with a cut of the purse
    {
        foreach (PTSoul member in partyMembers.ToList())
        {
            PTEmployeeRecord job = member.employment;
            if (job == null || job.loyalty > 0) continue;
            int cut = Mathf.FloorToInt(gold * DESERTION_CUT);
            ChangeGold(-cut);
            job.stolenTotal += cut;
            TotalStolen += cut;
            PTAdventureLog.Log(member.Name + " has deserted in the night, taking " + cut + " gold with them!");
            RemovePartyMember(member, EmploymentStatus.Deserted, "Deserted on day " + daysElapsed);
        }
    }

    bool CheckMutiny()                                                                                       //returns true if the party mutinied (game over)
    {
        if (partyMembers.Count < 2) return false;
        float average = AverageLoyalty;
        int nearBreaking = partyMembers.Count(m => m.employment != null && m.employment.loyalty < MUTINY_LOW_LOYALTY);
        if (average < MUTINY_AVERAGE_LOYALTY || nearBreaking >= 2)
        {
            TriggerGameOver("Fed up with empty promises, the party mutinies! They split your purse and leave you for the goblins.", "Mutiny");
            return true;
        }
        if (average < MUTINY_WARNING_LOYALTY || nearBreaking == 1)
        {
            PTSoul angriest = partyMembers.OrderBy(m => m.employment != null ? m.employment.loyalty : 100).First();
            PTAdventureLog.Log("WARNING: whispers of mutiny around the campfire. " + angriest.Name + " is the loudest. Pay them, or else.");
        }
        return false;
    }

    void RunNightlyTheft()                                                                               //each member may skim from the purse; greedier and less loyal members skim more often
    {
        int missing = 0;
        int biggestCut = 0;
        LastNightThief = null;
        foreach (PTSoul member in partyMembers)
        {
            PTEmployeeRecord job = member.employment;
            if (job == null || gold <= 0) continue;
            float chance = job.greed * (1f - job.loyalty / THEFT_LOYALTY_DIVISOR) * THEFT_SCALE * TheftMultiplier;
            if (Random.value < chance)
            {
                int cut = Mathf.Max(1, Mathf.FloorToInt(gold * Random.Range(THEFT_CUT_MIN, THEFT_CUT_MAX)));
                ChangeGold(-cut);
                job.stolenTotal += cut;
                job.stolenUnrecovered += cut;
                missing += cut;
                if (cut > biggestCut) { biggestCut = cut; LastNightThief = member; }
            }
        }
        LastNightMissing = missing;
        TotalStolen += missing;
    }

    void LogMorningLedger(int expectedGold)                                                              //the accountant counts the purse every morning
    {
        if (LastNightMissing > 0)
        {
            string clue = CurrentAccusation == Accusation.Bookkeeper && LastNightThief != null           //the Bookkeeper reads the ink smudges
                ? " The smudges in the ledger point to someone whose name starts with '" + LastNightThief.Name.Substring(0, 1) + "'."
                : " Someone in the party has sticky fingers.";
            PTAdventureLog.Log("Morning ledger: expected " + expectedGold + " gold, counted " + gold + ". " + LastNightMissing + " gold is missing!" + clue);
        }
        else
        {
            PTAdventureLog.Log("Morning ledger: the books balance.");
        }
    }
#endregion

#region Audits, firing and payroll toggles
    public void TryAudit(PTSoul suspect)                                                                     //search a suspect's belongings for stolen gold
    {
        if (!CanPerformTownAction("You can't audit the books in the middle of a battle!")) return;
        if (suspect == null || !partyMembers.Contains(suspect) || suspect.employment == null) return;
        if (PendingConfrontation != null)
        {
            PTAdventureLog.Log("First decide what to do about " + PendingConfrontation.Name + ".");
            return;
        }
        if (hasAudited)
        {
            PTAdventureLog.Log("You've already audited someone today. Any more and the whole party will walk.");
            return;
        }

        hasAudited = true;
        PTEmployeeRecord job = suspect.employment;
        if (job.stolenUnrecovered > 0)
        {
            float chance = Mathf.Clamp(AUDIT_BASE_CHANCE - (suspect.atrSense - 3) * AUDIT_CHANCE_PER_SENSE, AUDIT_MIN_CHANCE, AUDIT_MAX_CHANCE) + AuditBonus; //a Bookworm in the party helps
            if (Random.value < chance)
            {
                PendingConfrontation = suspect;
                ThievesCaughtThisRun++;
                PTAdventureLog.Log("Caught! " + suspect.Name + "'s purse is " + job.stolenUnrecovered + " gold heavier than their wages explain. What will you do?");
            }
            else
            {
                PTAdventureLog.Log("You go through " + suspect.Name + "'s things and find nothing. Either they're clean, or better at this than you.");
            }
        }
        else
        {
            job.ChangeLoyalty(LOYALTY_WRONGLY_ACCUSED);
            PTAdventureLog.Log(suspect.Name + " is innocent, and furious at being accused.");
        }
        UpdateUI();
    }

    public void ResolveConfrontation(Confrontation choice)                                                   //decide what happens to a caught thief
    {
        PTSoul thief = PendingConfrontation;
        if (thief == null || thief.employment == null) return;
        PTEmployeeRecord job = thief.employment;
        PendingConfrontation = null;

        switch (choice)
        {
            case Confrontation.DockPay:
                int recovered = job.stolenUnrecovered;
                job.stolenUnrecovered = 0;
                job.ChangeLoyalty(LOYALTY_DOCKED);
                TotalRecovered += recovered;
                ChangeGold(recovered);
                PTAdventureLog.Log("You take back " + recovered + " gold from " + thief.Name + ". They sulk, but they'll think twice.");
                break;
            case Confrontation.Forgive:
                job.stolenUnrecovered = 0;
                job.greed *= 0.5f;
                job.ChangeLoyalty(LOYALTY_FORGIVEN);
                PTAdventureLog.Log("You forgive " + thief.Name + ". Shamed, they swear it won't happen again.");
                break;
            case Confrontation.Fire:
                if (CannotBeFired(thief))                                                                //the Queen's nephew stays, whatever he does
                {
                    PendingConfrontation = thief;
                    PTAdventureLog.Log(thief.Name + " can't be fired. The Queen would never forgive you. Dock their pay or forgive them.");
                    return;
                }
                PTAdventureLog.Log("You throw " + thief.Name + " out of the party for theft.");
                FireMember(thief, true);
                break;
        }
        UpdateUI();
    }

    public int SeveranceFor(PTSoul member) => member != null ? member.dailywages : 0;                       //severance is one day's wage

    public void TryFire(PTSoul member)                                                                       //let a party member go
    {
        if (!CanPerformTownAction("You can't fire anyone in the middle of a battle!")) return;
        if (member == null || !partyMembers.Contains(member)) return;
        if (CannotBeFired(member))
        {
            PTAdventureLog.Log(member.Name + " can't be fired. Their aunt is the Queen.");
            return;
        }
        if (member == PendingConfrontation)
        {
            ResolveConfrontation(Confrontation.Fire);
            return;
        }
        FireMember(member, false);
        UpdateUI();
    }

    void FireMember(PTSoul member, bool caughtStealing)
    {
        if (!caughtStealing)
        {
            int severance = SeveranceFor(member);
            if (gold >= severance)
            {
                ChangeGold(-severance);
                TotalSeverancePaid += severance;
                PTAdventureLog.Log(member.Name + " is let go with " + severance + " gold in severance.");
            }
            else
            {
                ChangePartyLoyalty(LOYALTY_NO_SEVERANCE, member);
                PTAdventureLog.Log(member.Name + " is let go without severance. The rest of the party takes note.");
            }
        }
        string reason = caughtStealing ? "Fired for theft on day " : "Fired on day ";
        RemovePartyMember(member, EmploymentStatus.Fired, reason + daysElapsed);
    }

    public void TogglePay(PTSoul member)                                                                     //tick or untick a member for tonight's payroll
    {
        if (member == null || member.employment == null) return;
        member.employment.payTonight = !member.employment.payTonight;
        UpdateUI();
    }

    public int PayrollTonight => partyMembers.Where(m => m.employment == null || m.employment.payTonight).Sum(m => m.dailywages);
#endregion
}
