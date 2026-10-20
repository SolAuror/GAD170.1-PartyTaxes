# Party Taxes v0.8

> *A game about paying your empl— ...friends, to adventure with you on a quest to slay pests for the queen.*

You're the Queen's accountant, convicted of embezzlement and exiled to a goblin-infested frontier. You can't swing a sword, so you hire people who can, and pay them from a purse the Queen expects back with interest. Survive 28 days, pay her auditor every week, and you're pardoned. Slay the Goblin King too, and you're vindicated. Fail twice and you hang. Meanwhile your own party is skimming from you.

---------------------------------------------------------------------------------------

- **Engine:** Unity 6000.6.4f1 (Unity 6.6; upgraded from 6000.3.9f1)
  - *First open after the upgrade:* Unity re-imports the project, updates the built-in packages (URP, uGUI, Test Framework, Multiplayer Center) and `packages-lock.json`, and adds the editor revision to `ProjectSettings/ProjectVersion.txt`. Commit those generated changes. Scripts avoid the InstanceID-era APIs that became errors in 6.5 (`FindAnyObjectByType` instead of `FindFirstObjectByType`).
  - *Packages:* Input System 1.20.1 (1.18 and older fail to compile on 6.5+ because of the InstanceID to EntityId change). The Unity Version Control package (`com.unity.collab-proxy`) was removed for the same reason; the project uses Git. A compile error in any package stops every script loading, which shows up as magenta URP materials, so check the Console for compile errors first.
  - *TextMesh Pro:* the Examples & Extras folder was removed (its assets were too old for Unity 6.6). The two colour-gradient presets the UI uses now live in `Assets/PartyTaxes/UI/Gradients`. If TMP ever warns about its essential resources, use *Window > TextMeshPro > Import TMP Essential Resources*.
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

Every action has a clickable button in the **action bar** on the right of the screen (showing costs and what's available right now). Choosing jobs, hiring, payroll, audits and firing are mouse-only, from the **Job board**, **Tavern** and **Ledger** views. The keyboard shortcuts below still work. Combat plays itself; in battle the same keys trigger gold-spending interventions.

### Town
| Key | Action |
---------------------------------------------------------------------------------------
| `Space` | Sleep (advance day) |
| `E` | Open the job board |
| `F` | Open the tavern's hiring board |
| `Q` | Heal the party (cleric) |
| `1` | Bless the party (cleric) |
| `3` | Resurrect a fallen member *(unlocks day 7)* |
| `Escape` | Reset / restart the game |
| `4` | *(Debug mode only)* Give every party member 100 XP |

### Combat
---------------------------------------------------------------------------------------
| `1` | Hazard pay (+25% party damage this round) |
| `Q` | Potion for the most hurt member |
| `3` | Bribe the toughest enemy to leave |
| `F` | Focus fire on the biggest threat |
| `R` | Sound the retreat |
| `Space` | Combat speed: 1× / 2× / Skip |

---------------------------------------------------------------------------------------

## Gameplay Systems

### Starting a Run: Accusations, Audit Levels & Seeds
Each run starts on the setup screen. Pick **what you were accused of** (your starting archetype), an **Audit Level** (difficulty), and a random or **daily** seed. A daily run uses today's date, so everyone gets the same recruits, jobs and events.

| Accusation | Effect | Unlocked by |
|---|---|---|
| The Clerk | No advantages, no catches | — |
| The Embezzler | +100 starting gold, tribute +20% | Winning a run |
| The Scapegoat | Start with a Loyal Veteran on a normal wage | Surviving to day 14 |
| The Bookkeeper | The morning ledger names the thief's initial; starting loyalty 50 and 15 gold less | Catching 3 thieves (across runs) |

**Audit Levels** stack: 2 = tribute +8% · 3 = thieves 30% bolder · 4 = signing fees +50% · 5 = one more goblin in every job and tribute another +8%. Winning at a level unlocks the next. Score is multiplied by 1 + 0.25 × (level − 1). Progress (wins, best day, thieves caught, best score, max level) is saved in `PlayerPrefs`.

### The Queen's Tribute (win condition)
A run lasts **28 days**. At the end of every 7th day the Queen's auditor collects tribute: **250 / 450 / 650 / 900 gold**. It's taken *before* wages, so the Queen always gets paid first. Pay the fourth tribute on day 28 and you're **pardoned**; kill the Goblin King that day as well and you're **vindicated**.

Miss a tribute and you get a **strike**: the auditor seizes all your gold and drags your best-paid hero off as collateral. A second strike, or missing the final tribute, gets you **hanged**.

### Payroll & Loyalty
Every party member has **Loyalty** (0-100, visible in the Ledger) and wages due each night. In the Ledger you can tick or untick each member for tonight's payroll. At night they're paid in party order while the gold lasts.

| Event | Loyalty |
|---|---|
| Paid on time | +5 |
| Went unpaid | −15 |
| A comrade died in combat | −10 |
| Landed a killing blow | +3 |
| Blessed by the cleric | +10 |
| Watched someone fired without severance | −10 |
| Wrongly accused in an audit | −15 |
| Caught stealing: pay docked / forgiven | −10 / +15 |

A member at **0 loyalty deserts** in the night with 10% of your gold. If the party's **average loyalty drops below 20**, or **two members fall below 10**, the party **mutinies** (game over). The log warns you when loyalty is getting dangerous.

### Theft, the Morning Ledger & Audits
Each member has a hidden **Greed**, hinted at by their description (*honest face*, *hard to read*, *eyes your purse*). Each night, every member may skim 5-12% of your gold. The chance rises with greed and falls with loyalty. Each morning you count the purse: *"expected 212 gold, counted 187"*. You're not told who did it.

Once a day you can **audit** one member from the Ledger. If they've stolen since they were last caught, you'll probably catch them (harder for high-Sense characters). Then you choose to **dock their pay** (get the gold back), **forgive** them (more loyal, less greedy) or **fire** them. If they're innocent, they're furious (−15 loyalty).

### Tavern (hiring board) & Contracts
Three candidates are on the board each morning, already at your party's average level, with their stats, wage, greed hint and **traits** visible. Each can be signed on one of three **contracts** (fees ×1.5 if anyone in the party is Cowardly):

| Contract | Signing fee | Then |
|---|---|---|
| Day wage | 2 days' wages | Paid nightly, as usual |
| Loot share | 1 day's wage | No wage; takes **16% of every completed job** (pay + loot). +5 loyalty on days they get a cut, −5 on days they don't |
| Signing bonus | 5 days' wages | **60% of the wage**, for good |

### Traits
Every recruit has one trait, and 35% have two (never conflicting ones). There are 20, defined in `Resources/PartyTaxes/Traits.json`. Most are a trade-off: Light-fingered (steals more, loots 25% more), Honest, Loyal, Mercenary, Veteran (+2 levels, +30% wage), Coward (−30% wage, starts Cowardly), Tough, Brawler, Lucky, Haggler (cleric −25%), Pious, Bloodthirsty, Union Rep, Gambler (wage rerolled nightly), Drunkard (hungover after payday), **Nephew of the Queen** (can't be fired or seized, −10% tribute), Medic, Glory Hound, Bookworm (audits +15%), Frugal. Party-wide perks use the best one in the party.

### Raises
When a party member levels up, they **ask for their raise** instead of taking it (+15% of base wage per level). You can **accept** it (+5 loyalty), **refuse** it (−15 loyalty, wage stays), or **counter** with half: 60% chance minus 5% per Sense above 3, clamped to 30–80%. A failed counter means the full raise and −10 loyalty. Loot-share members don't ask.

### Night Events
Most nights (70%) something happens, drawn from 34 events in `Resources/PartyTaxes/Events.json`: tavern brawls, a sick mother, forged receipts, the rival accountant who framed you, poachers, the Queen's herald... Each has 1–3 choices; some are gambles. Some only appear from a certain day, with a certain trait in the party, or with enough members. Effects can change gold, loyalty, HP, greed, attributes, XP, wages, cowardice, the next tribute or the next job's pay, or make someone quit. An event appears at most once per run, and you decide in the morning.

**Decisions block the day:** a caught thief, a raise request or a night event must be answered before you take a job or sleep.


### Firing
You can fire anyone from the Ledger (it asks you to confirm). Severance is one day's wage; if you can't afford it, the rest of the party loses 10 loyalty.

### Day / Night Cycle
Each day you can perform town actions and take **one job** (or rest). Sleeping runs the night in order: **tribute (on tribute days) → payroll → desertions → mutiny check → rest → theft**, then a new morning with the ledger count, fresh tavern candidates and a new job board. Paid members recover 25% of their max HP overnight; unpaid members recover nothing.

### Job Board
Each morning **three contracts** are drawn (weighted) from the job pool, each showing its pay and risk. A **rest day** (no pay, +40% HP) is always available. Pay grows each week of the run.

| Job | From day | Risk | Enemies (× party size) | Pay (base + per week) | Notes |
|---|---|---|---|---|---|
| Scouting patrol | 1 | Low | 0.5–1 | 18 + 9 | No weekly escalation |
| Clear a goblin nest | 1 | Medium | 1–1.25 | 30 + 15 | |
| Escort the tax wagon | 2 | Low | 0.75–1 | 21 + 9 | Only a 50% chance of a fight; +5 loyalty |
| Raid the goblin hoard | 5 | High | 1.5–1.75 | 51 + 24 | |
| Hoblin war-camp | 8 | High | 1–1.25 | 66 + 27 | Enemies roll 3 levels above the party |
| Bounty: a Goblin Lord | 12 | High | 0.75–1 | 78 + 30 | Enemies +4 levels; +3 loyalty |
| **Slay the Goblin King** | 28 | High | boss + 0.75–1 guards | 400 | 14× HP, 3× attack boss. **No retreat, no bribes** |

Jobs that escalate get +1 max enemy each week from day 8. The pool is editable in the PTManager Inspector (*Job Board*).

### Combat
Battles **play themselves**. Every round the side with the higher average Sense goes first, then everyone acts in Sense order. XP from each kill is split evenly between party members. Winning pays the job plus any loot.

**Stance** (set in town) decides how the party fights:

| Stance | Damage dealt | Damage taken | Targets |
|---|---|---|---|
| Aggressive | ×1.15 | ×1.25 | The weakest enemy (finish kills) |
| Balanced | ×1 | ×1 | Random |
| Cautious | ×0.85 | ×0.8 | The biggest hitter |

**Interventions**, each once per round. There's a short pause at the start of every round to decide.

| Intervention | Cost | Effect |
|---|---|---|
| Hazard pay | 5g × members | +25% party damage this round, +2 loyalty each |
| Potion | 15g | Most hurt member +40 HP |
| Bribe | 15g + 2× its loot | The toughest non-boss enemy leaves (no loot or XP) |
| Focus fire | free | Everyone attacks the biggest threat until it falls |
| Sound the retreat | 10% of gold | The fight ends with no pay; members below 50 loyalty become Cowardly |

**Speed:** 1×, 2× or Skip (resolve instantly).

### Healing & Blessings
The cleric can heal the party once per day for a small gold fee, restoring HP to each member. A full blessing costs significantly more but fully restores HP, removes the Cowardly debuff and gives +10 loyalty. Cowardly members receive 30% reduced healing from normal heals.

### Resurrection
Unlocked on day 7. Pay 100 gold plus the fallen member's daily wages to resurrect a random dead party member. Resurrected members come back as themselves (same attributes, level, XP and employment record) at full health and 80 loyalty, with a permanent 10% penalty to max HP, attack and defense. They are **Marked by Death** and cannot become Cowardly.

### Leveling
Members gain XP from kills; each level needs `50 × (level+1) × √level` XP. Each level up grants **+1 to the race's affinity attribute** (Human: Might, Dwarf: Constitution, Elf: Sense, Halfling: Luck), **+15 max HP**, and heals 30% of max HP. It also raises daily wages by **15% of their starting wage**, so stronger parties cost more to keep.

### The Final Ledger
When the run ends, win or lose, the books open: outcome, score, tribute paid, wages, theft and recoveries, and every hire's record (kills, loot, wages, what they stole, and how they left). The biggest thief is finally named.

**Score** = (tribute paid + gold kept + 25 per surviving member − 25 per death + 250 if pardoned + 500 more for slaying the Goblin King) × the Audit Level multiplier. The Final Ledger also shows the accusation, Audit Level, seed and anything the run unlocked.


## Game Over Conditions

| Condition | Trigger |
---------------------------------------------------------------------------------------
| **Vindicated** *(best win)* | Slay the Goblin King on day 28, then pay the final tribute |
| **Pardoned** *(win)* | Pay the day-28 tribute |
| **Hanged** | Miss a second tribute, or miss the final one |
| **Mutiny** | Average loyalty below 20, or two members below 10 |
| **Abandoned** | Everyone has deserted, been fired, been seized or quit |
| **Slain** | All party members killed in combat |

---------------------------------------------------------------------------------------

## Script Architecture

Contains a 12 part partial manager class and random recruit generator attached to a Manager GameObject, a CharacterData script called PTSoul, employment records, and runtime-built UI.

### `PTManager` — partial class, 12 files

The central game controller. Owns all shared state (gold, party list, enemy list, day counter, UI references, balance constants) and is split into partial files for readability:

| File | Responsibility |
---------------------------------------------------------------------------------------
| `PTManager.cs` | Variables, constants, `GameState` (Town / Combat / GameOver / Victory), `Awake`/`Start`, input setup, shared helpers (`ChangeGold`, `UpdateUI`, `TriggerGameOver`, `SetSunIntensity`) |
| `PTManager.Party.cs` | Party formation (`GetNextPartyMemberTransform`), `SpawnRandomPartyMember`, `RemovePartyMember` (with a status and fate), `LevelUpTo`, `GetAveragePartyLevel` |
| `PTManager.Combat.cs` | The auto-combat coroutine (`RunCombat`), stances and targeting, interventions (`TryHazardPay`, `TryPotion`, `TryBribe`, `TryFocusFire`, `TryRetreat`), speed, enemy/boss spawning |
| `PTManager.Jobs.cs` | The job pool, `RefreshJobBoard`, `TryTakeJob`, `TryRestDay`, `CompleteJob` (+ loot-share cuts), the Goblin King |
| `PTManager.Run.cs` | `BeginRun(accusation, auditLevel, seed, daily)`, difficulty modifiers, `RecordRunEnd` (unlocks) |
| `PTManager.Traits.cs` | `RollTraits`, `GiveTraits`, party-wide trait perks, trait damage/loot helpers |
| `PTManager.Events.cs` | `DrawNightEvent`, `ResolveEvent`, event effects; raise negotiation (`PendingRaise`, `ResolveRaise`) |
| `PTManager.Town.cs` | `TrySleep` (the night sequence) + `RestParty`, `TryHeal`, `TryBless`, `TryResurrect`, `RestartRun`, `CanPerformTownAction` |
| `PTManager.Ledger.cs` | Employment, `RunPayroll`, loyalty events, `RunNightlyTheft`, `TryAudit` + `ResolveConfrontation`, `TryFire`, `ResolveDesertions`, `CheckMutiny`, run totals |
| `PTManager.Tribute.cs` | Tribute schedule, `CollectTribute` (strikes, seizure, execution), `TriggerVictory`, `RunScore` |
| `PTManager.Tavern.cs` | `RefreshTavern` (3 inactive candidate souls), `SigningFee`, `TryHire` |
| `PTManager.Actions.cs` | Read-only costs and availability for the action bar (`HealCost`, `FleeCost`, ...) |

Each input callback (`OnSleep`, `OnHeal`, ...) is a one-line wrapper around its `Try...` method, so keyboard and buttons share the same logic.

### `PTActionBar`
Builds its own overlay canvas at runtime (no scene setup needed; turn it off with `buildActionBar` on PTManager). Views: **Main** (town actions, stance), **Job board**, **Tavern** (hiring board), **Ledger** (payroll toggles, audits, firing), and **Combat** (interventions, speed). A caught thief forces the confrontation choice. `PTManager.UpdateUI()` calls `Refresh()`, which re-reads every label, cost and availability. Opens the Final Ledger when the run ends.

### `PTFinalLedgerPanel` / `PTUIFactory`
The end-of-run books (outcome, score, totals, per-hire table, biggest thief). `PTUIFactory` holds the shared runtime UI helpers (canvas, panel, row, label, button) so all generated UI looks the same.

### `PTContent`, `PTTrait`, `PTNightEvent` (Data)
`PTContent` loads `Resources/PartyTaxes/Traits.json` and `Events.json` with `JsonUtility`. **The balance sim reads the same files**, so content edits are immediately reflected in the tuning numbers. Trait fields are additive modifiers (missing = no effect); `special` names the few behaviours that need code.

### `PTRunRules` / `PTRunSetupPanel`
Accusation and Audit Level rules, unlock conditions, daily/random seeds and `PlayerPrefs` progress; the start-of-run setup screen.

### `PTJob` (Data Class)
A contract: name, description, risk, first day, weight, enemy count (× party size), escalation, enemy level bonus, ambush chance, pay (+ per week), loyalty bonus, and boss settings. `PTJob.DefaultPool()` and `PTJob.GoblinKing()` hold the tuned defaults; edit them in the PTManager Inspector.

### `PTEmployeeRecord` (Data Class)
A party member's employment file: loyalty, hidden greed, payroll toggle, nights unpaid, theft (total and unrecovered), kills, loot, wages received, deaths, and status/fate. Held on `PTSoul.employment` (runtime only) and in `PTManager.runLedger`, so the books keep everyone ever hired. It travels with `DeadCharacterData`, so a resurrected hero keeps their history.

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
- `LevelUp()` — Increases level, +1 to the affinity attribute, +15 max HP, heals 30%, raises wages by 15% of base wage
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

### **PTManager.Party.cs** — Party Formation

**Formation System:**
- `GetNextPartyMemberTransform(out Quaternion rotation)` — Repositions all party members in neat horizontal line, returns next available slot position

**Member Management:**
- `SpawnRandomPartyMember()` — Creates a starting party member with random stats/race and puts them on the payroll
- `RemovePartyMember(PTSoul member, EmploymentStatus status, string fate)` — Closes their employment file, saves the slain for resurrection, destroys the game object
- `LevelUpTo(PTSoul soul, int level, bool fullHeal)` — Levels a recruit or tavern candidate up to the party's average

**Utility:**
- `GetAveragePartyLevel()` — Calculates team power by averaging all member levels

---

### **PTManager.Town.cs** — Town Activities

**Helper:**
- `CanPerformTownAction()` — Safety check that prevents town actions during combat or after the run ends

**Daily Cycle:**
- `TrySleep()` — **(Spacebar)** Runs the night: tribute (on tribute days) → payroll → desertions → mutiny check → rest → theft → new morning (ledger count, fresh tavern, reset daily actions)

**Services:**
- `TryHeal()` — **(Q key)** Costs 10 gold per member, heals everyone for 50 HP, once per day
- `TryBless()` — **(1 key)** Costs 100 gold per member, fully heals, removes coward status, +10 loyalty, once per day
- `TryResurrect()` — **(3 key)** Unlocks day 7+, costs 100 gold + wages, revives a random dead character as themselves at 90% stats with "Marked by Death"
- `RestartRun()` — **(Escape key)** Reloads entire scene from scratch
- `OnRecruit` — **(F key)** Opens the tavern view (or hires the cheapest affordable candidate when there's no action bar)

---

### **PTManager.Ledger.cs / Tribute.cs / Tavern.cs** — The Accountant's Tools

- `RunPayroll()` — Pays ticked members in order while gold lasts; ±loyalty; records wages
- `ResolveDesertions()` / `CheckMutiny()` — Members at 0 loyalty leave with 10% of the gold; low average loyalty ends the run
- `RunNightlyTheft()` / `LogMorningLedger()` — Per-member skimming, then the morning count shows what went missing
- `TryAudit(member)` → `ResolveConfrontation(DockPay | Forgive | Fire)` — Once a day; innocent suspects resent it
- `TryFire(member)` / `TogglePay(member)` — Firing with severance, payroll ticks
- `CollectTribute()` — Pays the Queen or takes a strike (seizure, then the noose); `TriggerVictory()` on day 28
- `RefreshTavern()` / `SigningFee(candidate)` / `TryHire(index)` — The hiring board

---

### **PTManager.Combat.cs / Jobs.cs** — Jobs & Auto-Combat

**Jobs:**
- `RefreshJobBoard()` — Draws 3 weighted contracts available today (+ the Goblin King on day 28)
- `TryTakeJob(index)` — **(E opens the board)** One job per day: quiet escorts pay out at once, otherwise spawns the enemies (or the boss and guard) and starts combat
- `TryRestDay()` — Skip work, recover 40% HP
- `GetEncounterSize(job)` — Party size × the job's range, + weekly escalation for jobs that escalate

**Combat flow:**
- `StartCombat()` → `RunCombat()` coroutine — Rounds play automatically: a pause for interventions, initiative by average Sense, everyone acts in Sense order, with a delay per action scaled by speed
- `CheckCombatOver()` — Victory pays the job (`CompleteJob`), losing everyone ends the run
- `PickTarget()` — Focus target, else by stance (weakest / random / biggest hitter)
- `ProcessPartyAttack` / `ProcessEnemyAttack` — Damage with stance and hazard-pay multipliers; kills award loot, XP and loyalty

**Interventions:** `TryHazardPay()` **(1)**, `TryPotion()` **(Q)**, `TryBribe()` **(3)**, `TryFocusFire()` **(F)**, `TryRetreat()` **(R)**, `CycleCombatSpeed()` **(Space)**. Town keys are reused in battle.

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
Morning ledger (anything missing?) → Audit a suspect? → Hire at the Tavern / Heal / Bless → Set stance →
Pick a job (E) → Watch the fight, spend gold on interventions → Get paid → Set payroll in the Ledger →
Sleep (Space): tribute (every 7th day) → payroll → desertions/mutiny → rest → theft → Repeat until day 28
```

**Combat Round (automatic):**
1. Round pause: your window to buy interventions
2. Compare average Sense: higher side goes first (party wins ties)
3. Each side acts in order of highest → lowest Sense; party members pick targets by focus order or stance
4. Repeat until one side is gone (or you retreat)

**Stat Derivation Formulas:**
- **Max HP** = (60 + (Constitution × 10) + 15 per level gained) × stat multiplier
- **Attack** = Might × 4 × stat multiplier
- **Defense** = (Constitution × 2, +3 for party members) × stat multiplier
- **Stat multiplier** = 1.0 normally, 0.9 for souls Marked by Death
- **Crit Chance** = Luck × 3% (capped at 75%)
- **Crit Multiplier** = 1.5 + (Luck × 0.02) + (Might × 0.02) + (Sense × 0.02)
- **Damage Reduction** = Defense / (Defense + 50) — diminishing returns formula

**Enemy Spawn Scaling (`GetEncounterSize(job)`):**
- **Base Formula:** ceil(partyCount × job min) to ceil(partyCount × job max), inclusive (see the Job Board table)
- **Weekly Escalation** (jobs that escalate): from day 8, +1 to the maximum each week (day 8-14: +1, day 15-21: +2, ...)
- **Example:** goblin nest with a 4-member party: day 4 = 4-5 enemies; day 10 = 4-6; day 22 = 4-8
- **Enemy level:** party average level + the job's level bonus (enemies never roll above it)

---------------------------------------------------------------------------------------

## Balance Constants

**PTManager.cs**

| Constant | Value | Description |
---------------------------------------------------------------------------------------
| `BASE_HEAL_AMOUNT` | 50 | HP restored per party member (base healing) |
| `HEAL_COST_PER_MEMBER` | 10 | Gold cost per member for healing service |
| `BLESS_COST_PER_MEMBER` | 100 | Gold cost per member for blessing service |
| `BASE_RESURRECTION_COST` | 100 | Base gold cost for resurrection (+ wages) |
| `FLEE_COST_PERCENT` | 0.1 | 10% of gold dropped to attempt fleeing |
| `RESURRECTION_UNLOCK_DAY` | 7 | Day when resurrection becomes available |
| `REST_HEAL_PERCENT` | 0.25 | Share of max HP recovered overnight by paid members |
| `ESCALATION_START_DAY` / `ESCALATION_EVERY_DAYS` | 8 / 7 | +1 max enemy each week from day 8 |
| `REST_DAY_HEAL_PERCENT` *(Jobs)* | 0.4 | HP restored by a rest day |

**PTManager.Tribute.cs:** `RUN_LENGTH_DAYS` 28, `TRIBUTE_SCHEDULE` 250/450/650/900, `STRIKES_TO_EXECUTION` 2, score: +250 pardon, +500 vindicated, ±25 per survivor/death.

**PTManager.Combat.cs:** stances (Aggressive ×1.15 dealt / ×1.25 taken, Cautious ×0.85 / ×0.8), potion 15g / +40 HP, hazard pay 5g per member / +25% / +2 loyalty, bribe 15g + 2× loot, retreat 10% of gold (cowardice below 50 loyalty), `actionDelay` 0.35 s and `roundPause` 0.6 s *(inspector)*, 2× speed = 40% of the delays. Jobs: see the Job Board table.

**PTManager.Tavern.cs:** `TAVERN_CANDIDATES` 3, `SIGNING_FEE_DAYS` 2, `COWARD_FEE_MULTIPLIER` 1.5.

**PTManager.Ledger.cs:** loyalty start 60 (80 resurrected), the loyalty events in the table above, greed 0.1-0.9, theft chance `greed × (1 − loyalty/150) × 0.7`, theft cut 5-12%, desertion cut 10%, mutiny below 20 average or two below 10, audit catch chance `0.8 − 0.05 × (Sense − 3)` clamped to 40-90%.

**PTSoul.cs:** `PARTY_DEFENSE_BONUS` 3, `HP_PER_LEVEL` 15, `LEVEL_UP_HEAL_PERCENT` 0.3, `WAGE_RAISE_PER_LEVEL` 0.15 (of base wage), XP to next level `50 × (level+1) × √level`.

These values were tuned with the balance model in `Tools/BalanceSim`. Keep it in sync when you change them.

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
- **Wage Inflation:** Each level-up adds 15% of the starting wage, so a strong party costs more to keep
- **Debug Mode Toggle:** Set `debugMode = true` in PTManager inspector to see AI decisions and spawn calculations in console, and to use `4` to grant XP
- **Escape Individual Rolls:** Each party member independently rolls escape based on their agility vs average enemy agility (50% base ± difference)
- **Colorization Regex:** Adventure log uses regex patterns to auto-detect and colorize gold amounts, HP, enemy names, etc.
- **Theft:** Per member, per night: `greed × (1 − loyalty/150) × 0.7`. Unpaid members lose loyalty, so stiffing people makes theft worse
- **Initialization Guard:** `PTSoul.Start()` only sets up souls that weren't already set up by a spawner (`FinishSetup()`), so generated and resurrected data survives the first frame
- **UI Context Awareness:** Control display dynamically hides unavailable actions (e.g., Heal button disappears after daily use)
- **Prefab Fallback Chain:** Resurrection tries original prefab → first party prefab → logs warning if all fail

---------------------------------------------------------------------------------------

## ⚖️ Recent Balance Changes

### v0.8 - Phase 3: Variety
Traits, night events, raise negotiation, loot-share and signing-bonus contracts, accusations, Audit Levels 1–5, and daily/random seeds.

Balance model (Clerk, Audit Level 1, 2,000 runs each):

| Player | Won (vindicated) | Economic loss | Slain |
|---|---|---|---|
| *Accountant*: picks contracts and event choices by value, counters raises | 33% (21%) | 56% | 11% |
| *Naive*: day wage only, accepts every raise, first event choice | 28% | 13% | 59% |

- **Accusations** are within about 4 points of each other at every level (`--matrix`). **Audit Levels** step the accountant from about 31% wins to 25%, 20%, 15% and 11%
- **Variety** (`--variety`): by day 3 a run has shown about 11 different traits and 2 night events, and two runs share only 41% of their day-1–3 content
- Re-tuned because Phase 3 made the game easier: pure-upside traits now cost wage (Tough/Brawler/Lucky/Honest/Bloodthirsty +10%, Loyal +5%, Frugal −1 Constitution); loot share is 16% (12% was the strongest contract)
- The first Audit Level 5 rule tested ("one missed tribute and you hang") made the level unwinnable for a careful scripted player, so Level 5 adds a goblin per job and more tribute instead

### v0.7 - Phase 2: Jobs & Auto-Combat
Combat plays itself. You pick a job and a stance, then spend gold on interventions. The single "encounter" became a daily job board, and day 28 offers the optional Goblin King.

Balance model (2,000 runs per scripted player):

| Player | Won (vindicated) | Economic loss | Slain |
|---|---|---|---|
| *Accountant*: risk-aware jobs, Aggressive, potions, retreats when losing, takes the King when strong | 30% (21%) | 61% | 9% *(all by the King)* |
| *Naive*: best-paying job, never intervenes | 21% (0%) | 12% | 67% |
| *Hoarder*: two heroes, safest jobs, Cautious | 0% | 100% | 0% |

- Fights take about 17–22 s at 1× (90th percentile 27–34 s; ~1–2% go over 45 s), with 1–1.4 decisions each (target: ≤ 45 s, ≤ 6 decisions)
- Retreat made ordinary fights nearly riskless for careful players, so the **Goblin King allows no retreat**: taking him on is a real gamble (a full, healthy party wins ~70%)
- Re-tuned: job pay (≈0.6× the old commission for a standard nest), tribute 250/450/650/900, boss 14× HP / 3× attack
- Removed: per-member fleeing, the click-to-attack turn system, grace days (early boards only offer small jobs), the flat victory commission
- The adventure log catches up when messages pile up, so it keeps pace with auto-combat
- Hazard pay and bribes aren't modelled in the sim (they're optional gold-for-safety trades)

### v0.6 - Phase 1: The Ledger
Added the Queen's tribute and 28-day win, per-member payroll and loyalty, greed-driven theft with morning ledger counts and audits, firing, desertion and mutiny, the tavern hiring board, and the Final Ledger.

Balance model (`python3 Tools/BalanceSim/party_taxes_sim.py`, 2,000 runs per scripted player):

| Player | Pardoned | Economic loss (hanged / mutiny / abandoned) | Slain |
|---|---|---|---|
| *Accountant*: full party, banks for tribute, audits after thefts, rests when hurt | 26% | 54% | 20% |
| *Naive*: spends freely, never audits | 14% | 78% | 8% |
| *Hoarder*: never hires beyond two | 1% | 0% | 99% |

**Why the economy was re-tuned:** with 25% compounding raises and an XP curve that sped up late, a full party cost 419g/day against 224g income by day 28, so no tribute schedule was winnable. Now:
- Raises are linear: +15% of the starting wage per level
- XP to level is `50 × (level+1) × √level`
- Commission grows +25g per week
- +15 max HP per level
- Enemy escalation is +1 per week from day 8; max enemies is party × 1.25
- The weekly survival bonus is replaced by the tribute

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
- **Checking changes without Unity:** `dotnet run --project Tools/HeadlessTests` compiles the scripts against API stubs and runs 118 behaviour checks (against the real JSON content); `python3 Tools/BalanceSim/party_taxes_sim.py` reports balance (`--matrix` for accusations × levels, `--variety` for run-to-run variety)

---

**Party Taxes** - Built with Unity 6 | Input System | TextMesh Pro | Made for GAD170 Project 1
