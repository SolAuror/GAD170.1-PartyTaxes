using UnityEngine;

namespace PartyTaxes
{
    /// <summary>
    /// ScriptableObject that defines the attribute ranges for a soul type (e.g. Human, Elf, Dwarf).
    /// Create assets via: Right-click in Project → Create → PartyTaxes → Soul Type Data
    /// </summary>
    [CreateAssetMenu(fileName = "SoulTypeData", menuName = "PartyTaxes/Soul Type Data")]
    public class PTSoulTypeData : ScriptableObject
    {
        [Header("Type Identity")]
        [Tooltip("Must match the string used in soul.Type (e.g. 'Human', 'Elf').")]
        public string typeName = "Human";

        [Tooltip("If true, souls of this type are treated as enemies rather than party members.")]
        public bool isAdversary = false;

        [Header("Name Pool")]
        [Tooltip("Names to randomly pick from for this type. If empty, the generator's global name pool is used instead.")]
        public string[] names;

        [Header("Attribute Ranges (min inclusive, max inclusive)")]
        public Vector2Int mightRange        = new Vector2Int(1, 5);
        public Vector2Int agilityRange      = new Vector2Int(1, 5);
        public Vector2Int constitutionRange = new Vector2Int(1, 5);
        public Vector2Int senseRange        = new Vector2Int(1, 5);
        public Vector2Int luckRange         = new Vector2Int(1, 5);

        [Header("Wage Range Override")]
        [Tooltip("Set both to 0 to fall back to the generator's global wageRange.")]
        public Vector2Int wageRange = new Vector2Int(0, 0);

        [Header("Spawn Level Range")]
        [Tooltip("The party level range during which this type will spawn.")]
        public Vector2Int levelRange = new Vector2Int(1, 3);

        [Header("Rewards")]
        [Tooltip("Gold awarded to the party when a soul of this type is defeated.")]
        public Vector2Int goldRewardRange = new Vector2Int(5, 10);
        [Tooltip("XP pool awarded to the party when a soul of this type is defeated (split among members).")]
        public Vector2Int lifeXpRange = new Vector2Int(20, 40);

        [Header("Level Scaling (added per level above the minimum spawn level)")]
        [Tooltip("Bonus added to Might per level above levelRange.x. Use 0.5 to gain +1 every 2 levels.")]
        public float bonusMightPerLevel        = 0f;
        [Tooltip("Bonus added to Agility per level above 1.")]
        public float bonusAgilityPerLevel      = 0f;
        [Tooltip("Bonus added to Constitution per level above 1.")]
        public float bonusConstitutionPerLevel = 0f;
        [Tooltip("Bonus added to Sense per level above 1.")]
        public float bonusSensePerLevel        = 0f;
        [Tooltip("Bonus added to Luck per level above 1.")]
        public float bonusLuckPerLevel         = 0f;

        [Header("Level Up")]
        [Tooltip("Attribute that gains +1 each time a soul of this type levels up (race affinity).")]
        public SoulAttribute levelUpAttribute  = SoulAttribute.Constitution;

        [Header("Visual Appearance")]
        [Tooltip("Optional: Custom prefab for this soul type. If assigned, this prefab will be used instead of the base enemy prefab. Must have a PTSoul component.")]
        public GameObject customPrefab;

        /// <summary>
        /// Returns true if this type has a custom prefab assigned.
        /// </summary>
        public bool HasCustomPrefab => customPrefab != null;

        /// <summary>
        /// Applies randomly rolled attributes (within this type's ranges) to the given soul,
        /// then adds per-level bonuses based on the soul's rolled level (capped at maxLevel, e.g. the party level).
        /// Also sets soul.Type, isAdversary, level, goldReward and accumulatedLifeXp.
        /// </summary>
        public void ApplyAttributes(PTSoul soul, int maxLevel = int.MaxValue)
        {
            soul.Type               = typeName;
            soul.isAdversary        = isAdversary;
            soul.levelUpAttribute   = levelUpAttribute;
            soul.levelBonusHP       = 0;
            soul.level              = Mathf.Clamp(Random.Range(levelRange.x, levelRange.y + 1), 1, Mathf.Max(1, maxLevel));
            soul.goldReward         = Random.Range(goldRewardRange.x,  goldRewardRange.y  + 1);
            soul.accumulatedLifeXp  = Random.Range(lifeXpRange.x,      lifeXpRange.y      + 1);

            // Base attributes rolled from ranges
            soul.atrMight           = Random.Range(mightRange.x,        mightRange.y        + 1);
            soul.atrAgility         = Random.Range(agilityRange.x,      agilityRange.y      + 1);
            soul.atrConstitution    = Random.Range(constitutionRange.x,  constitutionRange.y + 1);
            soul.atrSense           = Random.Range(senseRange.x,        senseRange.y        + 1);
            soul.atrLuck            = Random.Range(luckRange.x,         luckRange.y         + 1);

            // Apply per-level bonuses for each level above the type's minimum spawn level (level is capped first)
            int levelsAboveMin = soul.level - levelRange.x;
            if (levelsAboveMin > 0)
            {
                soul.atrMight        += Mathf.RoundToInt(bonusMightPerLevel        * levelsAboveMin);
                soul.atrAgility      += Mathf.RoundToInt(bonusAgilityPerLevel      * levelsAboveMin);
                soul.atrConstitution += Mathf.RoundToInt(bonusConstitutionPerLevel * levelsAboveMin);
                soul.atrSense        += Mathf.RoundToInt(bonusSensePerLevel        * levelsAboveMin);
                soul.atrLuck         += Mathf.RoundToInt(bonusLuckPerLevel         * levelsAboveMin);
            }
        }

        /// <summary>
        /// Returns true if this data has a valid wage override (both values > 0).
        /// </summary>
        public bool HasWageOverride => wageRange.x > 0 && wageRange.y > 0;

        /// <summary>
        /// Returns true if this type has its own name pool defined.
        /// </summary>
        public bool HasNamePool => names != null && names.Length > 0;

        /// <summary>
        /// Returns a random name from this type's name pool, or null if the pool is empty.
        /// </summary>
        public string GenerateName()
        {
            if (!HasNamePool) return null;
            return names[Random.Range(0, names.Length)];
        }
    }
}
