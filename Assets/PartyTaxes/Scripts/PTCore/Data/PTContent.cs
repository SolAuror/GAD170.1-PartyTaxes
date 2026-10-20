using System.Collections.Generic;
using UnityEngine;

namespace PartyTaxes
{
    /// <summary>
    /// A character trait, loaded from Resources/PartyTaxes/Traits.json. Every number is an additive
    /// modifier, so a field a trait doesn't mention is 0 = no effect. "special" names a behaviour
    /// that needs code: union_rep, gambler, drunkard, bloodthirsty, glory_hound.
    /// </summary>
    [System.Serializable]
    public class PTTrait
    {
        public string id;
        public string name;
        public string description;
        public float weight;                       // relative chance to roll (0 or missing = 1)
        public string[] excludes;                  // trait ids this can't be combined with

        public float greed;                        // added to hidden greed
        public int wagePercent;                    // base wage change
        public int damagePercent;
        public int lootPercent;                    // gold from their kills
        public int might, agility, constitution, sense, luck;
        public int levelBonus;                     // arrives this many levels above the party
        public int startLoyalty;
        public int unpaidLoyaltyPercent;           // -50 halves the loyalty lost when unpaid
        public int paidLoyalty;                    // added to the loyalty gained when paid
        public int clericDiscountPercent;          // party-wide (the best one applies)
        public int blessLoyalty;
        public int tributeDiscountPercent;         // party-wide (the best one applies)
        public int auditBonusPercent;              // party-wide (the best one applies)
        public int potionPercent;                  // party-wide (the best one applies)
        public int restPercent;                    // party-wide (the best one applies)
        public bool startsCowardly;
        public bool cannotBeFired;
        public bool immuneToSeizure;
        public string special;

        public float Weight => weight > 0f ? weight : 1f;
        public bool Excludes(string otherId) => excludes != null && System.Array.IndexOf(excludes, otherId) >= 0;
    }

    /// <summary>What one outcome of a night-event choice does. Every field is optional (0 = nothing).</summary>
    [System.Serializable]
    public class PTEventEffect
    {
        public int gold;
        public int goldPercent;                    // percent of current gold (negative = lose it)
        public int partyLoyalty;
        public int subjectLoyalty;
        public int partyHealPercent;               // percent of max HP (negative = hurt, never fatal)
        public int subjectHealPercent;
        public int subjectCowardly;                // 1 = becomes Cowardly, -1 = cured
        public float subjectGreed;
        public int subjectMight;
        public int subjectConstitution;
        public int subjectXp;
        public int subjectWagePercent;             // permanent change to their base wage
        public bool subjectLeaves;
        public int tributeChange;                  // added to the next tribute
        public int nextJobPayPercent;              // bonus (or cut) on the next job's pay
        public string resultText;
    }

    [System.Serializable]
    public class PTEventChoice
    {
        public string label;
        public float chance;                       // chance of success (0 or missing = always)
        public PTEventEffect success;
        public PTEventEffect failure;

        public float Chance => chance > 0f ? chance : 1f;
        public int GoldCost => success != null && success.gold < 0 ? -success.gold : 0;
    }

    /// <summary>A night event, loaded from Resources/PartyTaxes/Events.json. "{name}" in text is the event's subject.</summary>
    [System.Serializable]
    public class PTNightEvent
    {
        public string id;
        public string title;
        public string text;
        public int minDay;
        public int maxDay;                         // 0 = no limit
        public float weight;                       // 0 or missing = 1
        public string requiresTrait;               // the subject must have this trait
        public int minParty;
        public PTEventChoice[] choices;

        public float Weight => weight > 0f ? weight : 1f;
    }

    [System.Serializable] class PTTraitFile { public PTTrait[] traits; }
    [System.Serializable] class PTEventFile { public PTNightEvent[] events; }

    /// <summary>
    /// Loads trait and event content from Resources/PartyTaxes. The balance sim in Tools/BalanceSim
    /// reads the same JSON files, so content and tuning can't drift apart.
    /// </summary>
    public static class PTContent
    {
        private static List<PTTrait> traits;
        private static Dictionary<string, PTTrait> traitsById;
        private static List<PTNightEvent> events;

        public static IReadOnlyList<PTTrait> Traits { get { Load(); return traits; } }
        public static IReadOnlyList<PTNightEvent> Events { get { Load(); return events; } }

        public static PTTrait Trait(string id)
        {
            Load();
            PTTrait trait;
            return id != null && traitsById.TryGetValue(id, out trait) ? trait : null;
        }

        private static void Load()
        {
            if (traits != null) return;
            traits = new List<PTTrait>();
            traitsById = new Dictionary<string, PTTrait>();
            events = new List<PTNightEvent>();

            TextAsset traitText = Resources.Load<TextAsset>("PartyTaxes/Traits");
            if (traitText != null)
            {
                PTTraitFile file = JsonUtility.FromJson<PTTraitFile>(traitText.text);
                if (file != null && file.traits != null)
                {
                    foreach (PTTrait trait in file.traits)
                    {
                        if (trait == null || string.IsNullOrEmpty(trait.id)) continue;
                        traits.Add(trait);
                        traitsById[trait.id] = trait;
                    }
                }
            }
            else
            {
                Debug.LogWarning("PTContent: Resources/PartyTaxes/Traits.json not found. Recruits will have no traits.");
            }

            TextAsset eventText = Resources.Load<TextAsset>("PartyTaxes/Events");
            if (eventText != null)
            {
                PTEventFile file = JsonUtility.FromJson<PTEventFile>(eventText.text);
                if (file != null && file.events != null)
                {
                    foreach (PTNightEvent nightEvent in file.events)
                    {
                        if (nightEvent != null && nightEvent.choices != null && nightEvent.choices.Length > 0) events.Add(nightEvent);
                    }
                }
            }
            else
            {
                Debug.LogWarning("PTContent: Resources/PartyTaxes/Events.json not found. There will be no night events.");
            }
        }
    }
}
