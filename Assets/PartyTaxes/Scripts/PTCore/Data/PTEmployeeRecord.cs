using UnityEngine;

namespace PartyTaxes
{
    public enum EmploymentStatus { Candidate, Employed, Slain, Deserted, Fired, Seized, Quit }
    public enum ContractType { DayWage, LootShare, SigningBonus }

    /// <summary>
    /// A party member's employment file: loyalty, greed, pay, and what they've been up to.
    /// Party members carry one on PTSoul.employment (enemies don't). PTManager also keeps every record
    /// in its run ledger, so the final books still show people who died, deserted or were fired.
    /// </summary>
    [System.Serializable]
    public class PTEmployeeRecord
    {
        public string name;
        public EmploymentStatus status = EmploymentStatus.Candidate;
        public string fate = "";                                   // how they left, e.g. "Slain by Gob the Goblin on day 9"

        [Range(0, 100)] public int loyalty = 60;                   // visible: paying on time raises it, stiffing them drops it
        [Range(0f, 1f)] public float greed = 0.5f;                 // hidden: only hinted at by GreedHint
        public bool payTonight = true;                             // payroll toggle, set from the ledger

        public int nightsUnpaid = 0;
        public int stolenTotal = 0;                                // everything they ever took (revealed in the final ledger)
        public int stolenUnrecovered = 0;                          // taken since they were last caught: what an audit can find
        public int kills = 0;
        public int goldLooted = 0;
        public int wagesReceived = 0;
        public int timesDied = 0;
        public int dayHired = 1;

        public ContractType contract = ContractType.DayWage;
        public System.Collections.Generic.List<string> traits = new System.Collections.Generic.List<string>();   // trait ids (see PTContent)
        public int requestedWage = 0;                              // a raise they've asked for after levelling up (0 = none)
        public bool hungover = false;                              // Drunkard: fights at -30% damage today
        public bool tookCutToday = false;                          // Loot share: got a cut of today's job
        public int killsThisFight = 0;                             // Bloodthirsty: did they land a kill?

        public bool IsEmployed => status == EmploymentStatus.Employed;
        public bool OnLootShare => contract == ContractType.LootShare;

        public bool HasTrait(string id) => traits.Contains(id);

        public bool HasSpecial(string special)
        {
            foreach (string id in traits)
            {
                PTTrait trait = PTContent.Trait(id);
                if (trait != null && trait.special == special) return true;
            }
            return false;
        }

        /// Sum of one trait modifier over this person's traits.
        public float TraitSum(System.Func<PTTrait, float> field)
        {
            float total = 0f;
            foreach (string id in traits)
            {
                PTTrait trait = PTContent.Trait(id);
                if (trait != null) total += field(trait);
            }
            return total;
        }

        public bool TraitFlag(System.Func<PTTrait, bool> flag)
        {
            foreach (string id in traits)
            {
                PTTrait trait = PTContent.Trait(id);
                if (trait != null && flag(trait)) return true;
            }
            return false;
        }

        /// Trait names for display, e.g. "Loyal, Veteran".
        public string TraitNames
        {
            get
            {
                System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
                foreach (string id in traits)
                {
                    PTTrait trait = PTContent.Trait(id);
                    if (trait != null) names.Add(trait.name);
                }
                return string.Join(", ", names);
            }
        }

        public string ContractName => contract == ContractType.LootShare ? "Loot share" : contract == ContractType.SigningBonus ? "Signing bonus" : "Day wage";

        public void ChangeLoyalty(int amount)
        {
            loyalty = Mathf.Clamp(loyalty + amount, 0, 100);
        }

        /// What the player can read about a character's greed.
        public string GreedHint => greed < 0.35f ? "honest face" : (greed > 0.65f ? "eyes your purse" : "hard to read");

        /// One word for how loyal they are, matching the mutiny thresholds in PTManager.Ledger.
        public string Mood => loyalty >= 80 ? "Devoted"
                            : loyalty >= 60 ? "Content"
                            : loyalty >= 40 ? "Uneasy"
                            : loyalty >= 20 ? "Resentful"
                            : "Mutinous";
    }
}
