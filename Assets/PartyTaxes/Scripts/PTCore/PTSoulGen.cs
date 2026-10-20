using UnityEngine;
using PartyTaxes;

public class PTSoulGen : MonoBehaviour                                                              //PTSoulGen class — responsible for generating random stats, 
{                                                                                                   //names, and types for both recruits and enemies, based on configurable pools and rules.
    [Header("Name Generation")]
    public string[] fullNames = { "Salen", "Loneth", "Dran", "Fayn", "Conti", "Gilaine", "Goro", "Horus", "Hester", "Juli" };

    [Header("Prefixes")]
    [Tooltip("Pool of prefixes to randomly assign. Each carries a stat modifier.")]
    public PTSoulPrefix[] prefixes = new PTSoulPrefix[]                                             //prefix tiers, 1-5 with stat mods
    {
        new PTSoulPrefix { prefixName = "Feeble",   attribute = SoulAttribute.Might,        modifier = -2 },
        new PTSoulPrefix { prefixName = "Weak",     attribute = SoulAttribute.Might,        modifier = -1 },
        new PTSoulPrefix { prefixName = "Strong",   attribute = SoulAttribute.Might,        modifier = 1 },
        new PTSoulPrefix { prefixName = "Fierce",   attribute = SoulAttribute.Might,        modifier = 2 },
        new PTSoulPrefix { prefixName = "Mighty",   attribute = SoulAttribute.Might,        modifier = 3 },

        new PTSoulPrefix { prefixName = "Sickly",   attribute = SoulAttribute.Constitution,  modifier = -2 },
        new PTSoulPrefix { prefixName = "Frail",    attribute = SoulAttribute.Constitution,  modifier = -1 },
        new PTSoulPrefix { prefixName = "Sturdy",   attribute = SoulAttribute.Constitution,  modifier = 1 },
        new PTSoulPrefix { prefixName = "Hardy",    attribute = SoulAttribute.Constitution,  modifier = 2 },
        new PTSoulPrefix { prefixName = "Ironclad", attribute = SoulAttribute.Constitution,  modifier = 3 },

        new PTSoulPrefix { prefixName = "Clumsy",   attribute = SoulAttribute.Agility,       modifier = -2 },
        new PTSoulPrefix { prefixName = "Sluggish", attribute = SoulAttribute.Agility,       modifier = -1 },
        new PTSoulPrefix { prefixName = "Quick",    attribute = SoulAttribute.Agility,       modifier = 1 },
        new PTSoulPrefix { prefixName = "Swift",    attribute = SoulAttribute.Agility,       modifier = 2 },
        new PTSoulPrefix { prefixName = "Blazing",  attribute = SoulAttribute.Agility,       modifier = 3 },

        new PTSoulPrefix { prefixName = "Dim",      attribute = SoulAttribute.Sense,         modifier = -2 },
        new PTSoulPrefix { prefixName = "Dull",     attribute = SoulAttribute.Sense,         modifier = -1 },
        new PTSoulPrefix { prefixName = "Sharp",    attribute = SoulAttribute.Sense,         modifier = 1 },
        new PTSoulPrefix { prefixName = "Keen",     attribute = SoulAttribute.Sense,         modifier = 2 },
        new PTSoulPrefix { prefixName = "Sage",     attribute = SoulAttribute.Sense,         modifier = 3 },

        new PTSoulPrefix { prefixName = "Cursed",   attribute = SoulAttribute.Luck,          modifier = -2 },
        new PTSoulPrefix { prefixName = "Unlucky",  attribute = SoulAttribute.Luck,          modifier = -1 },
        new PTSoulPrefix { prefixName = "Lucky",    attribute = SoulAttribute.Luck,          modifier = 1 },
        new PTSoulPrefix { prefixName = "Blessed",  attribute = SoulAttribute.Luck,          modifier = 2 },
        new PTSoulPrefix { prefixName = "Charmed",  attribute = SoulAttribute.Luck,          modifier = 3 },
    };

    [Tooltip("Chance (0-1) that a soul receives a prefix. 0 = never, 1 = always.")] 
    [Range(0f, 1f)]
    public float prefixChance = 0.35f;                                                            //prefix chances
    
    [Header("Enemy Spawning")]
    [Tooltip("Base prefab used to instantiate enemies. Must have a PTSoul component.")]
    public GameObject enemyBasePrefab;                                                           //enemy prefab template, must have PTSoul component

    [Header("Soul Types")]
    [Tooltip("Assign one PTSoulTypeData asset per soul type. Randomization will pick from this list.")]
    public PTSoulTypeData[] soulTypes;                                                         //Soul type data pool, defines attribute ranges and other rules for different types of souls
    
    [Header("Stat Ranges (Level 1)")]
    public Vector2Int wageRange = new Vector2Int(5, 20);                                    //base daily wage range for random recruits (can be overridden by soul type data)
    
    /// Returns a random name from the fullNames pool, with no type suffix.
    /// Used as a fallback when no type is known yet.
    public string GenerateRandomName()
    {
        return fullNames[Random.Range(0, fullNames.Length)];
    }

    /// Returns a name in the format "[Prefix] Name the TypeName" or "Name the TypeName" if no prefix.
    /// Falls back to the type's own name pool for the first name if one is set.
    public string GenerateNameForType(PTSoulTypeData typeData, PTSoulPrefix prefix = null)
    {
        string firstName = typeData.HasNamePool
            ? typeData.GenerateName()
            : fullNames[Random.Range(0, fullNames.Length)];
        string baseName = firstName + " the " + typeData.typeName;
        return prefix != null ? prefix.prefixName + " " + baseName : baseName;
    }

    /// Randomly picks a prefix from the pool based on prefixChance, applies its modifier
    /// to the soul's attributes, and returns it (or null if none was applied).
    public PTSoulPrefix TryApplyPrefix(PTSoul soul)
    {
        if (prefixes == null || prefixes.Length == 0) return null;
        if (Random.value > prefixChance) return null;
        PTSoulPrefix chosen = prefixes[Random.Range(0, prefixes.Length)];
        chosen.Apply(soul);
        return chosen;
    }
    
    /// Randomize all stats for a PTSoul character at level 1
    public void RandomizeStats(PTSoul soul)
    {
        if (soul == null) return;

        soul.level = 1;
        soul.currentXp = 0;

        // Pick a random non-adversary soul type and apply its attribute ranges
        PTSoulTypeData typeData = null;
        if (soulTypes != null && soulTypes.Length > 0)
        {
            System.Collections.Generic.List<PTSoulTypeData> friendlyTypes = new System.Collections.Generic.List<PTSoulTypeData>();
            foreach (PTSoulTypeData data in soulTypes)
                if (data != null && !data.isAdversary) friendlyTypes.Add(data);
            if (friendlyTypes.Count > 0)
                typeData = friendlyTypes[Random.Range(0, friendlyTypes.Count)];
        }

        if (typeData != null)
        {
            typeData.ApplyAttributes(soul);                                       // Apply type-based attribute ranges
            soul.level = Mathf.Max(soul.level, 1);                               // Ensure party members are at least level 1
            PTSoulPrefix prefix = TryApplyPrefix(soul);                           // Try to apply a random prefix, which may modify attributes and name
            soul.Name = GenerateNameForType(typeData, prefix);                    // Generate name based on type and prefix
            soul.dailywages = typeData.HasWageOverride                             // Use type-specific wage range if available
                ? Random.Range(typeData.wageRange.x, typeData.wageRange.y + 1)
                : Random.Range(wageRange.x, wageRange.y + 1);
        }
        else                                                                    // Fallback if no soul type assets are assigned
        {
            soul.Name            = GenerateRandomName();
            soul.Type            = "Unknown";
            soul.atrMight        = Random.Range(1, 6);
            soul.atrAgility      = Random.Range(1, 6);
            soul.atrConstitution = Random.Range(1, 6);
            soul.atrSense        = Random.Range(1, 6);
            soul.atrLuck         = Random.Range(1, 6);
            soul.dailywages      = Random.Range(wageRange.x, wageRange.y + 1);
        }

        soul.FinishSetup();                                          // Derive HP, attack, defense from attributes, start at full HP and update UI
    }

    /// Picks a random adversary PTSoulTypeData whose levelRange contains partyLevel.
    /// Falls back to the closest type if none match exactly.
    public PTSoulTypeData PickEnemyType(int partyLevel)
    {
        if (soulTypes == null || soulTypes.Length == 0) return null;

        // Collect all adversary types whose level range covers the party level
        System.Collections.Generic.List<PTSoulTypeData> candidates = new System.Collections.Generic.List<PTSoulTypeData>();
        foreach (PTSoulTypeData data in soulTypes)
            if (data != null && data.isAdversary && partyLevel >= data.levelRange.x && partyLevel <= data.levelRange.y)
                candidates.Add(data);

        if (candidates.Count > 0)
            return candidates[Random.Range(0, candidates.Count)];

        // Fallback: pick the adversary type with the closest max level
        PTSoulTypeData closest = null;
        int closestDist = int.MaxValue;
        foreach (PTSoulTypeData data in soulTypes)
        {
            if (data == null || !data.isAdversary) continue;
            int dist = Mathf.Abs(data.levelRange.y - partyLevel);
            if (dist < closestDist) { closestDist = dist; closest = data; }
        }
        return closest;
    }

    /// Randomizes all stats for an enemy soul based on party level, picking an
    /// appropriate adversary type from the soulTypes pool.
    public void RandomizeEnemyStats(PTSoul soul, int partyLevel)
    {
        if (soul == null) return;
        ApplyEnemyType(soul, PickEnemyType(partyLevel), partyLevel);
    }

    /// Rolls an enemy of the given type with its level capped at the party level (or a fully
    /// random "Unknown" adversary when no type data exists), then finalizes its stats.
    private void ApplyEnemyType(PTSoul soul, PTSoulTypeData typeData, int partyLevel)
    {
        if (typeData != null)
        {
            typeData.ApplyAttributes(soul, partyLevel);                      // Enemies can't spawn above party level
            PTSoulPrefix prefix = TryApplyPrefix(soul);
            soul.Name = GenerateNameForType(typeData, prefix);
        }
        else
        {
            soul.Name            = GenerateRandomName();
            soul.Type            = "Unknown";
            soul.isAdversary     = true;
            soul.level           = Mathf.Max(1, soul.level);
            soul.atrMight        = Random.Range(1, 6);
            soul.atrAgility      = Random.Range(1, 6);
            soul.atrConstitution = Random.Range(1, 6);
            soul.atrSense        = Random.Range(1, 6);
            soul.atrLuck         = Random.Range(1, 6);
        }

        soul.currentXp = 0;
        soul.FinishSetup();
    }

    /// Instantiates an enemy from enemyBasePrefab (or type-specific custom prefab),
    /// randomizes its stats for the given party level, parents it to the given transform,
    /// and returns the GameObject. Returns null if no valid prefab is available.
    public GameObject SpawnEnemy(Transform parent, Vector3 position, int partyLevel)
    {
        // Pick the enemy type first to determine which prefab to use
        PTSoulTypeData typeData = PickEnemyType(partyLevel);
        
        // Determine which prefab to instantiate (type-specific or base)
        GameObject prefabToUse = null;
        if (typeData != null && typeData.HasCustomPrefab)
        {
            prefabToUse = typeData.customPrefab;
        }
        else if (enemyBasePrefab != null)
        {
            prefabToUse = enemyBasePrefab;
        }
        else
        {
            Debug.LogWarning("PTSoulGen: No valid prefab found for enemy spawn.");
            return null;
        }

        // Instantiate the chosen prefab
        GameObject enemyObj = Instantiate(prefabToUse, position, Quaternion.identity, parent);
        PTSoul soul = enemyObj.GetComponent<PTSoul>();
        
        if (soul != null)
        {
            ApplyEnemyType(soul, typeData, partyLevel);                      // Apply stats using the picked type data
            enemyObj.name = soul.Name;
        }
        
        return enemyObj;
    }

    /// Returns the PTSoulTypeData whose typeName matches the given name, or null if not found.
    public PTSoulTypeData GetTypeData(string typeName)
    {
        if (soulTypes == null || string.IsNullOrEmpty(typeName)) return null;
        foreach (PTSoulTypeData data in soulTypes)
            if (data != null && data.typeName == typeName) return data;
        return null;
    }
    
    /// Create a completely random recruit from a base prefab template
    public GameObject CreateRandomRecruit(GameObject basePrefab, Transform parent, Vector3 position)
    {
        if (basePrefab == null) return null;
        
        GameObject newRecruit = Instantiate(basePrefab, position, Quaternion.identity, parent);
        PTSoul soul = newRecruit.GetComponent<PTSoul>();
        
        if (soul != null)
        {
            RandomizeStats(soul);
        }
        
        return newRecruit;
    }
    
    /// Randomize only the name of a PTSoul, keeping other stats intact
    public void RandomizeName(PTSoul soul)
    {
        if (soul == null) return;
        PTSoulTypeData typeData = GetTypeData(soul.Type);
        PTSoulPrefix prefix = TryApplyPrefix(soul);
        soul.Name = typeData != null ? GenerateNameForType(typeData, prefix) : GenerateRandomName();
        soul.UpdateUI();
    }
    
    /// Randomize only combat stats, keeping name and level intact
    public void RandomizeCombatStats(PTSoul soul)
    {
        if (soul == null) return;

        // Try to match the soul's existing type; fall back to a random non-adversary one
        PTSoulTypeData typeData = GetTypeData(soul.Type);
        if (typeData == null && soulTypes != null && soulTypes.Length > 0)
        {
            System.Collections.Generic.List<PTSoulTypeData> friendlyTypes = new System.Collections.Generic.List<PTSoulTypeData>();
            foreach (PTSoulTypeData data in soulTypes)
                if (data != null && !data.isAdversary) friendlyTypes.Add(data);
            if (friendlyTypes.Count > 0)
                typeData = friendlyTypes[Random.Range(0, friendlyTypes.Count)];
        }

        if (typeData != null)
        {
            int keptLevel = soul.level;                                      // ApplyAttributes re-rolls level, keep the current one
            typeData.ApplyAttributes(soul);
            soul.level = keptLevel;
            PTSoulPrefix prefix = TryApplyPrefix(soul);
            soul.Name = GenerateNameForType(typeData, prefix);
            soul.baseWage = typeData.HasWageOverride
                ? Random.Range(typeData.wageRange.x, typeData.wageRange.y + 1)
                : Random.Range(wageRange.x, wageRange.y + 1);
            soul.RecalculateWage();
        }
        else
        {
            // Fallback if no soul type assets are assigned
            soul.atrMight        = Random.Range(1, 6);
            soul.atrAgility      = Random.Range(1, 6);
            soul.atrConstitution = Random.Range(1, 6);
            soul.atrSense        = Random.Range(1, 6);
            soul.atrLuck         = Random.Range(1, 6);
            soul.baseWage        = Random.Range(wageRange.x, wageRange.y + 1);
            soul.RecalculateWage();
        }

        soul.FinishSetup();                                          // Derive HP, attack, defense from attributes
    }
}
