using UnityEngine;

namespace PartyTaxes
{
    public enum JobKind { Fight, Boss }
    public enum JobRisk { Low, Medium, High }

    /// <summary>
    /// A contract on the daily job board: who you fight, how many, how tough, and what it pays.
    /// Configure the pool in the PTManager Inspector (Job Board). The defaults below were tuned with Tools/BalanceSim.
    /// </summary>
    [System.Serializable]
    public class PTJob
    {
        public string jobName = "Clear a goblin nest";
        [TextArea(1, 3)] public string description = "";
        public JobKind kind = JobKind.Fight;
        public JobRisk risk = JobRisk.Medium;

        [Header("Availability")]
        [Tooltip("First day this job can appear on the board.")]
        public int minDay = 1;
        [Tooltip("Relative chance of appearing on the board.")]
        public float weight = 1f;

        [Header("Enemies")]
        [Tooltip("Enemy count range, as a multiple of party size (rounded up).")]
        public float enemiesMin = 1f;
        public float enemiesMax = 1.25f;
        [Tooltip("Adds +1 to the max enemy count each week from day 8.")]
        public bool escalates = true;
        [Tooltip("Enemies are rolled as if the party were this many levels higher.")]
        public int enemyLevelBonus = 0;
        [Tooltip("Chance (0-1) the job turns into a fight at all. Below 1, quiet jobs pay out without a battle.")]
        [Range(0f, 1f)] public float ambushChance = 1f;

        [Header("Pay")]
        public int basePay = 30;
        [Tooltip("Pay grows by this much each week of the run.")]
        public int payPerWeek = 15;
        [Tooltip("Loyalty bonus for everyone when the job is done.")]
        public int loyaltyBonus = 0;

        [Header("Boss (Kind = Boss only)")]
        public string bossName = "Grubnak the Goblin King";
        [Tooltip("The boss is rolled at party level + this, but never below bossMinLevel.")]
        public int bossLevelBonus = 3;
        public int bossMinLevel = 9;
        public float bossHealthMultiplier = 14f;
        public float bossAttackMultiplier = 3f;

        /// The default contracts; jobs appear on the board from their minDay.
        public static PTJob[] DefaultPool()
        {
            return new PTJob[]
            {
                new PTJob { jobName = "Scouting patrol", risk = JobRisk.Low, minDay = 1, weight = 1f,
                            description = "Count the goblins, kill a few. Small, safe, poorly paid.",
                            enemiesMin = 0.5f, enemiesMax = 1f, escalates = false, basePay = 18, payPerWeek = 9 },
                new PTJob { jobName = "Clear a goblin nest", risk = JobRisk.Medium, minDay = 1, weight = 1f,
                            description = "The bread and butter of frontier work.",
                            enemiesMin = 1f, enemiesMax = 1.25f, escalates = true, basePay = 30, payPerWeek = 15 },
                new PTJob { jobName = "Escort the tax wagon", risk = JobRisk.Low, minDay = 2, weight = 0.8f,
                            description = "Half the time nobody attacks. The party enjoys easy money.",
                            enemiesMin = 0.75f, enemiesMax = 1f, escalates = false, ambushChance = 0.5f, basePay = 21, payPerWeek = 9, loyaltyBonus = 5 },
                new PTJob { jobName = "Raid the goblin hoard", risk = JobRisk.High, minDay = 5, weight = 0.8f,
                            description = "A lot of goblins guarding a lot of gold.",
                            enemiesMin = 1.5f, enemiesMax = 1.75f, escalates = true, basePay = 51, payPerWeek = 24 },
                new PTJob { jobName = "Hoblin war-camp", risk = JobRisk.High, minDay = 8, weight = 0.7f,
                            description = "Fewer enemies, but a tier tougher than you're used to.",
                            enemiesMin = 1f, enemiesMax = 1.25f, escalates = true, enemyLevelBonus = 3, basePay = 66, payPerWeek = 27 },
                new PTJob { jobName = "Bounty: a Goblin Lord", risk = JobRisk.High, minDay = 12, weight = 0.6f,
                            description = "Hunt down a lord and their guard. Well paid, and the party loves a bounty.",
                            enemiesMin = 0.75f, enemiesMax = 1f, escalates = false, enemyLevelBonus = 4, basePay = 78, payPerWeek = 30, loyaltyBonus = 3 },
            };
        }

        /// The optional day-28 boss contract.
        public static PTJob GoblinKing()
        {
            return new PTJob { jobName = "Slay the Goblin King", kind = JobKind.Boss, risk = JobRisk.High, minDay = 28, weight = 0f,
                               description = "End the goblin threat for good and clear your name. There is no retreat from his hall.",
                               enemiesMin = 0.75f, enemiesMax = 1f, escalates = false, basePay = 400, payPerWeek = 0 };
        }
    }
}
