using UnityEngine;

namespace PartyTaxes
{
    public enum EmploymentStatus { Candidate, Employed, Slain, Deserted, Fired, Seized }

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

        public bool IsEmployed => status == EmploymentStatus.Employed;

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
