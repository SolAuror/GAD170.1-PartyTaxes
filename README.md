# Party Taxes v0.5

> *A game about paying your empl— ...friends, to adventure with you on a quest to slay pests for the queen.*

---------------------------------------------------------------------------------------

- **Engine:** Unity 6000.3.9f1
- **Input:** Unity Input System package
- **UI:** TextMesh Pro

---------------------------------------------------------------------------------------

## 📋 Table of Contents

- [Controls](#controls)
- [Gameplay Systems](#gameplay-systems)
- [Game Over Conditions](#game-over-conditions)
- [Script Architecture](#script-architecture)
- [Complete Method Breakdown](#-complete-method-breakdown-laymans-terms)
- [Key Gameplay Flows](#key-gameplay-flows)
- [Balance Constants](#balance-constants-ptmanagercs)
- [Design Patterns & Architecture](#design-patterns--architecture-decisions)
- [Implementation Notes](#implementation-notes)

---------------------------------------------------------------------------------------

## Controls

Every action has a clickable button in the **action bar** on the right of the screen (showing costs and what's available right now). The keyboard shortcuts below still work.

### Town
| Key | Action |
---------------------------------------------------------------------------------------
| `Space` | Sleep (advance day) |
| `E` | Go on an encounter |
| `F` | Recruit a new party member |
| `Q` | Heal the party (cleric) |
| `1` | Bless the party (cleric) |
| `3` | Resurrect a fallen member *(unlocks day 7)* |
| `Escape` | Reset / restart the game |
| `4` | *(Debug mode only)* Give every party member 100 XP |

### Combat
---------------------------------------------------------------------------------------
| `Left Click` | Attack |
| `R` | Run away |

---------------------------------------------------------------------------------------

## Gameplay Systems

### Party Management
Recruit and manage a party of up to 4 adventurers. Each member has stats (HP, attack, defense), a level, and daily wages you must pay. New recruits are leveled up to match your party's average and join at full health. Members who die are remembered and can return through resurrection.

### Gold & Economy
Gold is your core resource. You earn it by defeating enemies and through weekly survival bonuses. You spend it on wages, healing, blessings, recruitment, and resurrection. Party members may also steal gold while you sleep, with higher theft rates and amounts when wages go unpaid.

### Day / Night Cycle
Each day you can perform town actions and trigger one encounter. Sleeping advances the day, pays wages, rests the party, rolls for theft, and resets all daily actions. A paid party recovers 25% of its max HP overnight; an unpaid party sleeps poorly and recovers nothing. Consecutive days without paying wages increase theft chance and inch towards mutiny.

### Combat
Encounters spawn a scaled group of enemies based on party size and days elapsed (see *Enemy Spawn Scaling*). Each party member attacks a random enemy, then surviving enemies counter-attack. XP from each kill is split evenly between all party members. Defeating all enemies awards gold loot and a commission bonus.

### Healing & Blessings
The cleric can heal the party once per day for a small gold fee, restoring HP to each member. A full blessing costs significantly more but fully restores HP and removes the Cowardly debuff. Cowardly members receive 30% reduced healing from normal heals.

### Recruitment
Recruit a new party member for half your current total daily wages. Cowardly members inflate recruitment costs by 50%.

### Fleeing
Escape from battle at the cost of 10% of your current gold. All surviving (non-resurrected) party members are branded as Cowardly. No loot or commission is earned.

### Resurrection
Unlocked on day 7. Pay 100 gold plus the fallen member's daily wages to resurrect a random dead party member. Resurrected members come back as themselves (same attributes, level and XP) at full health, with a permanent 10% penalty to max HP, attack and defense, and are **Marked by Death** — they cannot become Cowardly.

### Leveling
Members gain XP from kills. Each level up grants **+1 to the race's affinity attribute** (Human: Might, Dwarf: Constitution, Elf: Sense, Halfling: Luck), **+10 max HP**, and heals 30% of max HP, but also raises daily wages by 25%, so stronger parties cost more to maintain. Enemies that level up mid-fight (by killing party members) grow the same way.


## Game Over Conditions

| Condition | Trigger |
---------------------------------------------------------------------------------------
| **Mutiny** | Wages unpaid for 3 consecutive days |
| **Total party wipe** | All party members killed in combat |

---------------------------------------------------------------------------------------

## Script Architecture

Contains a 5 part partial manager class and random recruit generator attached to a Manager GameObject, a CharacterData script called PTSoul, and a runtime-built action bar.

### `PTManager` — partial class, 5 files

The central game controller. Owns all shared state (gold, party list, enemy list, day counter, UI references, balance constants) and is split into partial files for readability:

| File | Responsibility |
---------------------------------------------------------------------------------------
| `PTManager.cs` | Variables, constants, `GameState` (Town / Combat / GameOver), `Awake`/`Start`, input setup, shared helpers (`ChangeGold`, `UpdateUI`, `TriggerGameOver`, `SetSunIntensity`) |
| `PTManager.Party.cs` | Party formation (`GetNextPartyMemberTransform`), `AddPartyMember`, `RemovePartyMember`, `LevelUpToPartyAverage`, `GetAveragePartyLevel` |
| `PTManager.Combat.cs` | `TryEncounter` + `GetEncounterSize`, `TryAttack`, `TryRunAway`, turn phases |
| `PTManager.Town.cs` | `TrySleep` + `RestParty`, `TryHeal`, `TryBless`, `TryRecruit`, `TryResurrect`, `RestartRun`, `CanPerformTownAction` |
| `PTManager.Actions.cs` | Read-only costs and availability for the action bar (`HealCost`, `RecruitCost`, `FleeCost`, ...) |

Each input callback (`OnSleep`, `OnHeal`, ...) is a one-line wrapper around its `Try...` method, so keyboard and buttons share the same logic.

### `PTActionBar`
Builds its own overlay canvas at runtime with a button for every action (no scene setup needed; turn it off with `buildActionBar` on PTManager). `PTManager.UpdateUI()` calls `Refresh()`, which re-reads labels, costs and which actions are allowed.

### `PTSoul`
Represents a single character — party member or enemy. Owns stats (HP, attack, defense, level, XP, wages) and status flags (`isCowardly`, `markedByDeath`, `isAdversary`). Handles its own damage, healing, blessing, XP, and level-up logic, and drives its own UI elements (name, health bar, XP bar). `PTManager` holds lists of `PTSoul` instances and calls into them to run gameplay.

### `PTSoulGen`
Centralized character generation system that creates both party members and enemies. Uses ScriptableObject data assets (PTSoulTypeData) to define races with configurable attribute ranges, level brackets, and rewards. Handles name generation with optional prefixes ("Mighty Salen the Dwarf"), stat randomization, and smart enemy scaling based on party level.

### `PTAdventureLog`
Singleton scrollable narrative log. Any script calls `PTAdventureLog.Log(message)` to append a rich-text entry. Handles auto-scrolling, entry count limits, and custom color tags for enemies (`<enemy>`), party names (`<partyname>`), etc.

### `DeadCharacterData`
Serializable snapshot class defined alongside `PTManager`. Captures a fallen `PTSoul`'s name, attributes, level, XP, wages and original prefab reference at the moment of death. Used exclusively by the resurrection system to restore the character with a configurable stat penalty.

### `PTSoulTypeData` (ScriptableObject)
Configuration asset for each race/creature type (Human, Elf, Goblin, etc.). Defines attribute ranges, spawn level brackets, name pools, gold/XP rewards, wage ranges, and per-level scaling bonuses. Supports both friendly (party member) and adversary (enemy) types.

### `PTSoulPrefix` (Data Class)
Defines stat-modifying prefixes like "Mighty" (+3 Might), "Feeble" (-2 Might), etc. Applied randomly during character generation with a 35% default chance. Each prefix targets one of the five attributes and applies a modifier clamped between -2 and +3.

---------------------------------------------------------------------------------------

## Methods Breakdown

### **PTSoul.cs** — Individual Character

**Combat Methods:**
- `DealDamage()` — Rolls the dice for an attack, checks for critical hits, calculates damage output
- `TakeDamage(int damage)` — Reduces incoming damage based on defense (diminishing returns), subtracts from health
- `FinishSetup()` — Derives stats, fills HP and marks the soul as initialized; spawners call it so `Start()` won't overwrite generated or resurrected data
- `WasCrit` — Simple yes/no check: was the last attack a critical hit?

**Healing Methods:**
- `Heal(int healAmount)` — Restores health points; cowards get 30% less healing due to shame
- `RestoreHP(int amount)` — Plain HP restore with no cowardice penalty (used for overnight rest)
- `Bless()` — Premium healing that fully restores HP AND removes coward status

**Character Growth:**
- `GainXP(int xpAmount)` — Adds experience points, automatically triggers level-up if threshold reached
- `LevelUp()` — Increases level, +1 to the affinity attribute, +10 max HP, heals 30%, raises wages by 25%
- `ChangeAttribute(SoulAttribute attribute, int amount)` — Increases/decreases specific stat, recalculates derived combat values
- `RecalculateStats()` — Converts 5 base attributes (plus level HP bonus and stat multiplier) into HP, attack, defense, crit chance, crit multiplier

**UI Methods:**
- `UpdateUI()` — Refreshes all on-screen displays: name, health bar, XP bar, level number

---

### **PTManager.cs** — Main Game Controller

**Core Systems:**
- `CalculateTotalDailyWages()` — Adds up how much gold the entire party demands each day
- `ChangeGold(int amount)` — Adds/removes gold, plays coin sound, updates UI, clamps at zero
- `ChangeXP(int xpAmount, string source)` — Splits XP equally among all living party members

**UI & Display:**
- `UpdateUI()` — Updates gold display (red when broke), day counter, battle status, party name
- `UpdateControlsDisplay()` — Shows context-sensitive keyboard controls (different in town vs combat)
- `SetSunIntensity(float intensity)` — Makes the sun brighter (daytime) or darker (nighttime)

**Game States:**
- `TriggerGameOver(string message)` — Ends the game and displays death message

---

### **PTManager.Party.cs** — Party Formation & Recruitment

**Formation System:**
- `GetNextPartyMemberTransform(out Quaternion rotation)` — Repositions all party members in neat horizontal line, returns next available slot position

**Member Management:**
- `SpawnRandomPartyMember()` — Creates new character with random stats/race, adds to party, auto-levels to match team
- `RemovePartyMember(PTSoul member)` — Removes from party list, saves data for resurrection, destroys game object
- `LevelUpToPartyAverage(PTSoul newMember)` — New recruits instantly level up to match party's average level, then heal to full

**Utility:**
- `GetAveragePartyLevel()` — Calculates team power by averaging all member levels

---

### **PTManager.Town.cs** — Town Activities

**Helper:**
- `CanPerformTownAction()` — Safety check that prevents town actions during combat

**Daily Cycle:**
- `TrySleep()` — **(Spacebar)** Advances to next day, pays wages, rests the party (if paid), checks for theft, awards weekly bonuses, resets daily action flags

**Services:**
- `TryHeal()` — **(Q key)** Costs 10 gold per member, heals everyone for 50 HP, once per day
- `TryBless()` — **(1 key)** Costs 100 gold per member, fully heals + removes coward status, once per day
- `TryRecruit()` — **(F key)** Costs half of daily wages (more with cowards), spawns random character
- `TryResurrect()` — **(3 key)** Unlocks day 7+, costs 100 gold + wages, revives a random dead character as themselves at 90% stats with "Marked by Death"
- `RestartRun()` — **(Escape key)** Reloads entire scene from scratch

---

### **PTManager.Combat.cs** — Battle System

**Enemy Spawning:**
- `TryEncounter()` — **(E key)** Spawns a group of enemies sized by `GetEncounterSize()`, switches to the Combat state, starts turn-based combat

**Turn Queue System:**
- `StartCombat()` — Clears old combat data, announces battle start, begins first round
- `ResetCombatState()` — Clears all turn queues and tracking when battle ends
- `StartRound()` — Sorts everyone by Sense stat, compares averages to determine who goes first
- `AdvancePartyTurn()` — Goes through party one by one, waits for player to press Attack for each
- `RunEnemyPhase()` — All enemies attack automatically in order of highest Sense, no player input

**Attack Processing:**
- `ProcessPartyAttack(PTSoul attacker)` — Your character attacks random enemy, rolls damage, awards gold/XP if kill
- `ProcessEnemyAttack(PTSoul enemyAttacker)` — Enemy attacks random party member, gives enemies XP if they kill someone

**Victory/Defeat:**
- `OnAllEnemiesDefeated()` — Awards commission gold, enables sleeping, returns to town
- `TryAttack()` — **(Left Click)** Executes active party member's attack when it's their turn (clicks on UI buttons are ignored)

**Escape System:**
- `TryRunAway()` — **(R key)** Costs 10% gold, each member rolls escape chance based on agility vs enemy agility, escapees become cowards

---

### **PTAdventureLog.cs** — Combat Log System

**Display Management:**
- `AddLogEntry(string message)` — Posts timestamped message, auto-colorizes keywords, scrolls to bottom, limits to 20 entries
- `ColorizeMessage(string message)` — Scans text for keywords (gold, HP, enemy, coward, death) and wraps in color tags
- `UpdateLogDisplay()` — Rebuilds entire log display when using single text mode
- `AddVisualEntry()` — Spawns new text object for multi-entry layout mode
- `RebuildLayout()` — Forces Unity to recalculate scroll box size, handles auto-scrolling
- `IsNearBottom()` — Checks if scroll position is within 10% of bottom

**Utility:**
- `ClearLog()` — **Static method** - Wipes all log entries
- `Log(string message)` — **Static method** - Public interface for adding messages from any script

---

### **PTSoulGen.cs** — Character Generator

**Name Generation:**
- `GenerateRandomName()` — Returns random name from default pool (Salen, Dran, Horus, etc.)
- `GenerateNameForType(typeData, prefix)` — Formats as "[Prefix] FirstName the RaceName" (e.g., "Mighty Goro the Dwarf")
- `RandomizeName(PTSoul soul)` — Re-rolls character's name while keeping all stats

**Prefix System:**
- `TryApplyPrefix(PTSoul soul)` — 35% chance to give character random stat-modifying prefix

**Party Member Creation:**
- `RandomizeStats(PTSoul soul)` — Creates party member from scratch: picks friendly race, rolls attributes, applies prefix, sets wages, starts at level 1
- `CreateRandomRecruit()` — **Full pipeline:** spawns prefab, runs RandomizeStats, returns finished character
- `RandomizeCombatStats(PTSoul soul)` — Re-rolls build, keeps level/name but randomizes all attributes

**Enemy Creation:**
- `PickEnemyType(int partyLevel)` — Chooses appropriate enemy type whose level range matches party's level
- `RandomizeEnemyStats(PTSoul soul, int partyLevel)` — Creates enemy: picks type, caps level at party level, rolls attributes plus per-level bonuses, applies prefix
- `SpawnEnemy()` — **Full pipeline:** spawns enemy prefab, runs RandomizeEnemyStats, returns finished enemy

**Utility:**
- `GetTypeData(string typeName)` — Looks up race configuration by name (e.g., "Human", "Goblin")

---

### **PTSoulTypeData.cs** — Race Configuration (ScriptableObject)

**Core Method:**
- `ApplyAttributes(PTSoul soul, int maxLevel)` — Rolls character creation: sets race, enemy status, affinity, random level in range (capped at `maxLevel`), random attributes in ranges, adds per-level scaling above the minimum level, sets rewards

**Properties:**
- `HasWageOverride` — Returns true if this race has custom wage ranges set
- `HasNamePool` — Returns true if this race has custom first names list
- `GenerateName()` — Returns random name from this race's name pool

---

### **PTSoulPrefix.cs** — Character Prefix System

**Application:**
- `Apply(PTSoul soul)` — Applies modifier to chosen attribute via `PTSoul.ChangeAttribute` (e.g., "Mighty" adds +3 to Might), clamped 1-99

---

### **DeadCharacterData** — Resurrection Data

**Constructor:**
- `DeadCharacterData(PTSoul soul, GameObject prefab)` — Saves snapshot of all stats, name, race, level, wages before death

**Restoration:**
- `ApplyTo(PTSoul soul, float statPenalty)` — Restores saved attributes to the new body, applies a lasting 10% stat multiplier, marks as "Marked by Death", makes fearless

---------------------------------------------------------------------------------------

## Key Gameplay Flows

**Daily Loop:**
```
Wake Up → See Wages → Find Enemies (E) → Fight → Win → Get Gold → 
Recruit (F) / Heal (Q) / Bless (1) → Sleep (Space): pay wages, rest → Repeat
```

**Combat Turn Order:**
1. Compare average Sense: higher side goes first
2. Each phase: characters act in order of highest → lowest Sense
3. Party phase: player manually clicks Attack for each member
4. Enemy phase: all enemies attack automatically
5. New round starts, repeat until victory/defeat

**Stat Derivation Formulas:**
- **Max HP** = (60 + (Constitution × 10) + 10 per level gained) × stat multiplier
- **Attack** = Might × 4 × stat multiplier
- **Defense** = (Constitution × 2, +3 for party members) × stat multiplier
- **Stat multiplier** = 1.0 normally, 0.9 for souls Marked by Death
- **Crit Chance** = Luck × 3% (capped at 75%)
- **Crit Multiplier** = 1.5 + (Luck × 0.02) + (Might × 0.02) + (Sense × 0.02)
- **Damage Reduction** = Defense / (Defense + 50) — diminishing returns formula

**Enemy Spawn Scaling (`GetEncounterSize`):**
- **Grace days (1-3):** exactly one enemy per party member
- **Base Formula:** partyCount to ceil(partyCount × 1.5), inclusive
- **Daily Escalation:** from day 7, +1 to the maximum every 3 days (day 7-9: +1, day 10-12: +2, ...)
- **Example:** 4-member party on day 4 = 4-6 enemies; day 10 = 4-8 enemies

---------------------------------------------------------------------------------------

## Balance Constants (PTManager.cs)

| Constant | Value | Description |
---------------------------------------------------------------------------------------
| `DAYS_UNTIL_MUTINY` | 3 | Days without paying wages before game over |
| `BASE_HEAL_AMOUNT` | 50 | HP restored per party member (base healing) |
| `HEAL_COST_PER_MEMBER` | 10 | Gold cost per member for healing service |
| `BLESS_COST_PER_MEMBER` | 100 | Gold cost per member for blessing service |
| `BASE_RESURRECTION_COST` | 100 | Base gold cost for resurrection (+ wages) |
| `THEFT_CHANCE_BROKE` | 0.25 | 25% theft chance when wages unpaid |
| `THEFT_CHANCE_NORMAL` | 0.1 | 10% theft chance when wages paid |
| `THEFT_PERCENT_BROKE` | 0.2 | 20% of gold stolen when broke |
| `THEFT_PERCENT_NORMAL` | 0.1 | 10% of gold stolen normally |
| `FLEE_COST_PERCENT` | 0.1 | 10% of gold dropped to attempt fleeing |
| `RESURRECTION_UNLOCK_DAY` | 7 | Day when resurrection becomes available |
| `WEEKLY_BONUS_INTERVAL` | 7 | Days between weekly survival bonuses |
| `REST_HEAL_PERCENT` | 0.25 | Share of max HP recovered overnight when wages are paid |
| `ENCOUNTER_GRACE_DAYS` | 3 | Days with one enemy per party member |
| `ENCOUNTER_MAX_MULTIPLIER` | 1.5 | Max enemies = party size × this (rounded up), before escalation |
| `ESCALATION_START_DAY` / `ESCALATION_EVERY_DAYS` | 7 / 3 | +1 max enemy every 3 days from day 7 |

PTSoul.cs: `PARTY_DEFENSE_BONUS` 3, `HP_PER_LEVEL` 10, `LEVEL_UP_HEAL_PERCENT` 0.3, `WAGE_GROWTH_PER_LEVEL` 1.25.

These values were tuned with the balance model in `Tools/BalanceSim` — keep it in sync when you change them.

---------------------------------------------------------------------------------------

## Design & Architecture Decisions

**Partial Classes:** PTManager is split across 4 files (Combat, Town, Party, Core) to organize code by responsibility domain while maintaining shared state.

**Data-Driven Character Generation:** PTSoulTypeData ScriptableObjects allow designers to configure new races/enemies without touching code. Each type defines attribute ranges, level brackets, rewards, and scaling.


**Turn-Based Initiative:** Each round, all combatants are sorted by Sense attribute. Average Sense determines phase order (party vs enemy), creating tactical value for high-Sense characters.

**Singleton Adventure Log:** Static PTAdventureLog.Log() allows any script to append messages without coupling, while automatic colorization maintains visual consistency.

**Resurrection Snapshot Pattern:** DeadCharacterData captures a character's attributes at death, allowing resurrection with a lasting stat penalty while preserving identity.

**Game State:** `PTManager.State` (Town / Combat / GameOver) decides which actions are allowed, replacing the old `canSleep` / `gameOver` flags. Per-day limits (`hasFought`, `hasHealed`, `hasBeenBlessed`) stay as flags.

**Status Flag System:** Boolean flags (isCowardly, markedByDeath, isAdversary) on PTSoul enable complex interactions without inheritance (e.g., cowards reduce healing, marked characters can't become cowards).

**Diminishing Returns Defense:** Formula defense/(defense+50) ensures high defense is valuable but never reaches immunity, maintaining tactical balance at all levels.

---------------------------------------------------------------------------------------

## Implementation Notes

- **Gold Safety:** All gold deductions use `Mathf.Max(gold - amount, 0)` to prevent negative values
- **Auto-Scroll Intelligence:** Log only auto-scrolls if user was already near bottom (within 10% threshold)
- **Formation Recentering:** When adding party members, entire formation recalculates to keep the group centered on spawn point
- **Prefix Rarity:** 35% chance to apply prefix ensures they feel special without being overwhelming
- **Enemy Level Capping:** Enemies never spawn above party's average level, preventing impossible encounters; the cap is applied before per-level attribute bonuses
- **Wage Inflation:** Each level-up increases wages by 25%, creating escalating economic pressure
- **Debug Mode Toggle:** Set `debugMode = true` in PTManager inspector to see AI decisions and spawn calculations in console, and to use `4` to grant XP
- **Escape Individual Rolls:** Each party member independently rolls escape based on their agility vs average enemy agility (50% base ± difference)
- **Colorization Regex:** Adventure log uses regex patterns to auto-detect and colorize gold amounts, HP, enemy names, etc.
- **Theft Escalation:** Being broke increases both theft chance (10%→25%) and the share stolen (10%→20%) to create pressure
- **Initialization Guard:** `PTSoul.Start()` only sets up souls that weren't already set up by a spawner (`FinishSetup()`), so generated and resurrected data survives the first frame
- **UI Context Awareness:** Control display dynamically hides unavailable actions (e.g., Heal button disappears after daily use)
- **Prefab Fallback Chain:** Resurrection tries original prefab → first party prefab → logs warning if all fail

---------------------------------------------------------------------------------------

## ⚖️ Recent Balance Changes

### v0.5 - Phase 0: Stabilise
Balance model (`python3 Tools/BalanceSim/party_taxes_sim.py`, 2,000 runs per strategy):

| | v0.4 | v0.5 |
|---|---|---|
| Median days survived (recruit when affordable) | 5 | 14 |
| Median days survived (careful) | 6 | 13 |
| Day-1 wipes | 13% | 0.4–2.4% |
| Best strategy | never recruit | recruit |

**Problems:** levelling gave no stats (attribute points were only spendable in debug) while raising wages and enemy tier; HP never recovered except via the cleric; a full party faced up to 2× its size in enemies, so recruiting was punished; day 1 was lethal 13% of the time.

**Changes:**
- Level ups grant +1 affinity attribute, +10 max HP and a 30% heal; the +3 party defense bonus is no longer lost on level up
- Paid parties rest 25% of max HP overnight
- Max enemies is party × 1.5 (was × 2); 3 grace days with one enemy per member
- Enemy per-level bonuses now apply (they were dead data); level cap applied first
- Theft cut when broke is now larger than normal (was inverted)

**Fixes:** resurrection restores the character's own attributes and keeps its penalty; new recruits no longer start with negative XP; level-up HP is no longer random; clicking action buttons doesn't also trigger an attack.

**Still open (Phase 1):** every simulated run still ends in a wipe, none in mutiny. Money only becomes the real pressure once the Queen's tribute arrives. See `Docs/DesignReview_v0.4.md`.

### v0.4 - Combat Scaling Adjustment
**Problem Identified:** With spawn formula `partyCount * 3`, a 2-member party faced 2-6 goblins on day 1. Action economy heavily favored enemies (6 attacks vs 2 per round), resulting in party struggling against full groups.

**Analysis:**
- Average Goblin: 70 HP, 6 attack, 2 defense
- Average Human: 90 HP, 8 attack, 9 defense (+3 party bonus)
- 2 humans vs 6 goblins: Each human takes ~3 hits/round = 15 damage
- Humans survive ~6 rounds but need ~27+ hits to eliminate all goblins
- Result: Enemies' numerical advantage overwhelming party

**Solution:** Reduced spawn multiplier from **3x to 2x** party count
- **Old:** `Random.Range(partyCount, partyCount * 3)` → 2-member party = 2-6 enemies
- **New:** `Random.Range(partyCount, partyCount * 2)` → 2-member party = 2-4 enemies
- Daily escalation remains: +1 enemy per day after day 7
- Enemy stat ranges unchanged; only quantity adjusted

**Result:** Improved survivability while maintaining challenge curve. Parties can win early encounters without perfect RNG, allowing progression to unlock healing, recruitment, and resurrection systems.

---------------------------------------------------------------------------------------

## 📝 Development Notes

- **Character Generation Pipeline:** PTSoulGen → PTSoulTypeData → PTSoul → UI
- **Combat Flow:** OnEncounter → StartCombat → StartRound → Party Phase → Enemy Phase → Victory/Defeat
- **Resurrection Pipeline:** Death → DeadCharacterData → Resurrection → ApplyTo with penalties
- **Log Message Flow:** Any script → PTAdventureLog.Log() → ColorizeMessage → AddVisualEntry → Auto-scroll
- **Gold Transaction Path:** Any system → ChangeGold() → OnGoldChanged event → UpdateUI (+ action bar refresh) + Sound
- **Checking changes without Unity:** `dotnet run --project Tools/HeadlessTests` compiles the scripts against API stubs and runs behaviour checks; `python3 Tools/BalanceSim/party_taxes_sim.py` reports balance

---

**Party Taxes** - Built with Unity 6 | Input System | TextMesh Pro | Made for GAD170 Project 1
