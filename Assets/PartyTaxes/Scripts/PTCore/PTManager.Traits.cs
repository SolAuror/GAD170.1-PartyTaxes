using System.Collections.Generic;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Traits Part: rolling traits for recruits and the party-wide helpers that trait
/// effects use. Trait data lives in Resources/PartyTaxes/Traits.json (see PTContent).
public partial class PTManager
{
    private const float SECOND_TRAIT_CHANCE = 0.35f;                                                         //every recruit has one trait, 35% have two
    private const float HUNGOVER_CHANCE = 0.3f;                                                              //Drunkard: chance to be hungover after payday
    private const float HUNGOVER_DAMAGE = 0.7f;                                                              //...and fight at 70% damage the next day
    private const int BLOODTHIRSTY_NO_KILL_LOYALTY = -10;                                                    //Bloodthirsty: a won fight without a kill
    private const int UNION_REP_EXTRA_LOYALTY = -5;                                                          //Union Rep: everyone else's extra loss when they go unpaid
    private const int GLORY_HOUND_LOYALTY = 5;                                                               //Glory Hound: +5 for high-risk wins, -5 on rest days

    List<string> RollTraits()                                                                                //one weighted trait, sometimes a compatible second
    {
        List<string> rolled = new List<string>();
        PTTrait first = WeightedTrait(null);
        if (first == null) return rolled;
        rolled.Add(first.id);
        if (Random.value < SECOND_TRAIT_CHANCE)
        {
            PTTrait second = WeightedTrait(first);
            if (second != null) rolled.Add(second.id);
        }
        return rolled;
    }

    PTTrait WeightedTrait(PTTrait alreadyHas)                                                                //a weighted pick that respects "excludes"
    {
        float total = 0f;
        foreach (PTTrait trait in PTContent.Traits)
        {
            if (Compatible(trait, alreadyHas)) total += trait.Weight;
        }
        if (total <= 0f) return null;
        float roll = Random.value * total;
        PTTrait picked = null;
        foreach (PTTrait trait in PTContent.Traits)
        {
            if (!Compatible(trait, alreadyHas)) continue;
            picked = trait;
            roll -= trait.Weight;
            if (roll <= 0f) break;
        }
        return picked;
    }

    static bool Compatible(PTTrait trait, PTTrait other)
    {
        if (other == null) return true;
        return trait.id != other.id && !trait.Excludes(other.id) && !other.Excludes(trait.id);
    }

    /// Gives a freshly generated soul its traits: attributes, greed, wage and cowardice. Level bonuses
    /// are applied by the caller (TraitLevelBonus) when it levels the soul to the party.
    void GiveTraits(PTSoul soul, List<string> traitIds, bool wagePremium = true)
    {
        PTEmployeeRecord job = CreateEmploymentRecord(soul);
        job.traits.Clear();
        job.traits.AddRange(traitIds);

        soul.ChangeAttribute(SoulAttribute.Might, Mathf.RoundToInt(job.TraitSum(t => t.might)), false);
        soul.ChangeAttribute(SoulAttribute.Agility, Mathf.RoundToInt(job.TraitSum(t => t.agility)), false);
        soul.ChangeAttribute(SoulAttribute.Constitution, Mathf.RoundToInt(job.TraitSum(t => t.constitution)), false);
        soul.ChangeAttribute(SoulAttribute.Sense, Mathf.RoundToInt(job.TraitSum(t => t.sense)), false);
        soul.ChangeAttribute(SoulAttribute.Luck, Mathf.RoundToInt(job.TraitSum(t => t.luck)), false);
        job.greed = Mathf.Clamp01(job.greed + job.TraitSum(t => t.greed));

        if (soul.baseWage <= 0) soul.baseWage = soul.dailywages;
        float wagePercent = wagePremium ? job.TraitSum(t => t.wagePercent) : Mathf.Min(0f, job.TraitSum(t => t.wagePercent));   //the Scapegoat's veteran waives the premium
        soul.baseWage = Mathf.Max(1, Mathf.RoundToInt(soul.baseWage * (1f + wagePercent / 100f)));
        if (job.TraitFlag(t => t.startsCowardly)) soul.isCowardly = true;

        soul.RecalculateStats();
        soul.currentHP = soul.maxHP;
        soul.RecalculateWage();
        soul.UpdateUI();
    }

    static int TraitLevelBonus(PTSoul soul) => soul.employment != null ? Mathf.RoundToInt(soul.employment.TraitSum(t => t.levelBonus)) : 0;

    float PartyBestTrait(System.Func<PTTrait, float> field)                                                  //party-wide perks use the best one in the party
    {
        float best = 0f;
        foreach (PTSoul member in partyMembers)
        {
            if (member.employment != null) best = Mathf.Max(best, member.employment.TraitSum(field));
        }
        return best;
    }

    float TraitDamageMultiplier(PTSoul member)                                                               //trait damage bonus, and the Drunkard's hangover
    {
        if (member.employment == null) return 1f;
        float multiplier = 1f + member.employment.TraitSum(t => t.damagePercent) / 100f;
        return member.employment.hungover ? multiplier * HUNGOVER_DAMAGE : multiplier;
    }

    int TraitLoot(PTSoul member, int gold) =>
        member.employment == null ? gold : Mathf.RoundToInt(gold * (1f + member.employment.TraitSum(t => t.lootPercent) / 100f));

    float ClericDiscount => PartyBestTrait(t => t.clericDiscountPercent) / 100f;
    float TributeDiscount => PartyBestTrait(t => t.tributeDiscountPercent) / 100f;
    float AuditBonus => PartyBestTrait(t => t.auditBonusPercent) / 100f;
    float PotionBonus => PartyBestTrait(t => t.potionPercent) / 100f;
    float RestBonus => PartyBestTrait(t => t.restPercent) / 100f;

    public bool CanBeFired(PTSoul member) => !CannotBeFired(member);

    static bool CannotBeFired(PTSoul member) => member != null && member.employment != null && member.employment.TraitFlag(t => t.cannotBeFired);
}
