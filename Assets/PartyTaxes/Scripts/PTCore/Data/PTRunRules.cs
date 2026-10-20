using UnityEngine;

namespace PartyTaxes
{
    /// <summary>What you were accused of: the starting archetype for a run.</summary>
    public enum Accusation { Clerk, Embezzler, Scapegoat, Bookkeeper }

    /// <summary>
    /// Descriptions, unlock conditions and difficulty rules for accusations and Audit Levels,
    /// plus the small amount of progress kept between runs (PlayerPrefs). Tuned with Tools/BalanceSim.
    /// </summary>
    public static class PTRunRules
    {
        public const int MAX_AUDIT_LEVEL = 5;

        public static string Name(Accusation accusation)
        {
            switch (accusation)
            {
                case Accusation.Embezzler:  return "The Embezzler";
                case Accusation.Scapegoat:  return "The Scapegoat";
                case Accusation.Bookkeeper: return "The Bookkeeper";
                default:                    return "The Clerk";
            }
        }

        public static string Description(Accusation accusation)
        {
            switch (accusation)
            {
                case Accusation.Embezzler:  return "You did take the money. Start with 100 extra gold, but the auditor wants 20% more.";
                case Accusation.Scapegoat:  return "Someone owes you. Start with a Loyal Veteran who works for a normal wage.";
                case Accusation.Bookkeeper: return "You read ledgers like tea leaves: the morning count names the thief's initial. The party resents being watched (loyalty 50) and you start 15 gold poorer.";
                default:                    return "An honest clerk, wrongly accused. No advantages, no catches.";
            }
        }

        public static string UnlockCondition(Accusation accusation)
        {
            switch (accusation)
            {
                case Accusation.Embezzler:  return "Win a run";
                case Accusation.Scapegoat:  return "Survive to day 14";
                case Accusation.Bookkeeper: return "Catch 3 thieves (across runs)";
                default:                    return "";
            }
        }

        public static bool IsUnlocked(Accusation accusation)
        {
            switch (accusation)
            {
                case Accusation.Embezzler:  return Wins >= 1;
                case Accusation.Scapegoat:  return BestDay >= 14;
                case Accusation.Bookkeeper: return ThievesCaught >= 3;
                default:                    return true;
            }
        }

        /// Cumulative difficulty rules for each Audit Level.
        public static string LevelDescription(int level)
        {
            switch (level)
            {
                case 2: return "The Queen's tribute is 8% higher.";
                case 3: return "...and thieves are 30% bolder.";
                case 4: return "...and signing fees are 50% higher.";
                case 5: return "...and every job has one more goblin, and tribute is another 8% higher.";
                default: return "The standard audit.";
            }
        }

        public static float TributeMultiplier(int level, Accusation accusation)
        {
            float multiplier = 1f;
            if (level >= 2) multiplier *= 1.08f;
            if (level >= 5) multiplier *= 1.08f;
            if (accusation == Accusation.Embezzler) multiplier *= 1.2f;
            return multiplier;
        }

        public static float TheftMultiplier(int level) => level >= 3 ? 1.3f : 1f;
        public static float FeeMultiplier(int level) => level >= 4 ? 1.5f : 1f;
        public static int ExtraEnemies(int level) => level >= 5 ? 1 : 0;
        public static float ScoreMultiplier(int level) => 1f + 0.25f * (level - 1);

        public static int StartingGoldBonus(Accusation accusation) =>
            accusation == Accusation.Embezzler ? 100 : accusation == Accusation.Bookkeeper ? -15 : 0;

        public static int StartingLoyalty(Accusation accusation, int normal) => accusation == Accusation.Bookkeeper ? 50 : normal;

        // ── Progress between runs (PlayerPrefs) ───────────────────────
        private const string KEY_MAX_LEVEL = "PT_MaxAuditLevel";
        private const string KEY_WINS = "PT_Wins";
        private const string KEY_BEST_DAY = "PT_BestDay";
        private const string KEY_THIEVES = "PT_ThievesCaught";
        private const string KEY_BEST_SCORE = "PT_BestScore";

        public static int MaxAuditLevel => Mathf.Clamp(PlayerPrefs.GetInt(KEY_MAX_LEVEL, 1), 1, MAX_AUDIT_LEVEL);
        public static int Wins => PlayerPrefs.GetInt(KEY_WINS, 0);
        public static int BestDay => PlayerPrefs.GetInt(KEY_BEST_DAY, 0);
        public static int ThievesCaught => PlayerPrefs.GetInt(KEY_THIEVES, 0);
        public static int BestScore => PlayerPrefs.GetInt(KEY_BEST_SCORE, 0);

        /// Records a finished run and returns what it unlocked (empty if nothing).
        public static System.Collections.Generic.List<string> RecordRun(bool won, int auditLevel, int day, int thievesCaught, int score)
        {
            System.Collections.Generic.List<string> unlocked = new System.Collections.Generic.List<string>();
            bool[] before = new bool[4];
            for (int i = 0; i < 4; i++) before[i] = IsUnlocked((Accusation)i);

            if (won)
            {
                PlayerPrefs.SetInt(KEY_WINS, Wins + 1);
                if (auditLevel >= MaxAuditLevel && auditLevel < MAX_AUDIT_LEVEL)
                {
                    PlayerPrefs.SetInt(KEY_MAX_LEVEL, auditLevel + 1);
                    unlocked.Add("Audit Level " + (auditLevel + 1) + " unlocked!");
                }
            }
            if (day > BestDay) PlayerPrefs.SetInt(KEY_BEST_DAY, day);
            PlayerPrefs.SetInt(KEY_THIEVES, ThievesCaught + thievesCaught);
            if (score > BestScore)
            {
                PlayerPrefs.SetInt(KEY_BEST_SCORE, score);
                unlocked.Add("New best score!");
            }
            PlayerPrefs.Save();

            for (int i = 0; i < 4; i++)
            {
                if (!before[i] && IsUnlocked((Accusation)i)) unlocked.Add(Name((Accusation)i) + " unlocked!");
            }
            return unlocked;
        }

        /// A short, shareable random seed (always positive).
        public static int RandomSeed() => Mathf.Abs(System.Environment.TickCount % 1000000);

        /// Today's daily-run seed, the same for everyone: yyyymmdd.
        public static int DailySeed()
        {
            System.DateTime today = System.DateTime.Now;
            return today.Year * 10000 + today.Month * 100 + today.Day;
        }
    }
}
