# Party Taxes — Design Review & Replayability Plan (v0.4 → itch release)

> **Pitch (keep this, it's great):** You're the Queen's accountant, convicted of embezzlement and exiled to a goblin-infested frontier. You can't swing a sword, so you spend her gold hiring people who can — and who are robbing you too.

This review is based on the code and data as they are now (`Assets/PartyTaxes/Scripts/PTCore`, the `SoulType_*.asset` files, `Prefab_Management.prefab`), not the README. Where the README and the code disagree, the code wins and the gap is listed in §3.

Numbers marked 📊 come from `Tools/BalanceSim/party_taxes_sim.py`, a Monte Carlo model of the v0.4 rules (2,000 runs per player strategy). Run it with `python3 Tools/BalanceSim/party_taxes_sim.py`.

---

## 1. TL;DR

| # | Finding | Severity |
|---|---|---|
| 1 | **The thing the game is about doesn't decide the game.** 📊 0 of 6,000 simulated runs ended in mutiny or bankruptcy. 100% ended in a combat wipe, at a median of **day 5–7**. Money is never the real pressure. | 🔴 Core |
| 2 | **Levelling is a pure penalty.** A level-up gives +1 `atrPoints` that can only be spent with the debug key, and raises wages by 25% (compounding). Enemy tier also scales with party level. Getting XP makes you weaker and poorer. | 🔴 Core |
| 3 | **Combat has no decisions.** Click Attack once per party member per round, with a random target. 📊 12–26 clicks per fight, and the only alternative is Run Away. | 🔴 Core |
| 4 | **Theft has no counterplay.** It's a hidden global dice roll, when it should be the most interesting system in the game: *who* is stealing, and what do you do about it? | 🟠 Theme |
| 5 | **No goal, no arc, no meta.** Runs are endless until death, and every run uses the same town, the same 4 races and the same goblin ladder. Nothing changes between runs. | 🟠 Replay |
| 6 | **The accountant has almost no levers.** You can't see candidates before hiring, choose who to hire, fire anyone, pay people individually, negotiate or audit. | 🟠 Theme |

**Recommended direction:** make the *ledger* the game. Combat becomes a short, mostly automatic spectacle that you influence **with money**. Your real decisions are hiring, payroll, loyalty and the Queen's tribute. Wrap that in a ~30-minute run with a win condition, and add data-driven variety (traits, events, jobs) for replayability.

---

## 2. What's working — keep and build on

- **The premise and tone.** "Accountant forced to outsource heroism" is a sharp, marketable hook, and the adventure log already has the right dry voice.
- **Employees who rob you.** Theft plus the threat of mutiny is a unique tension, and almost no party RPG makes your own party an antagonist. Lean all the way into it.
- **Data-driven soul types.** `PTSoulTypeData` ScriptableObjects and the prefix system are the right skeleton for traits, events and jobs. You can add a lot of content without new code.
- **Partial-class `PTManager`.** The split by domain is readable for a first project, and good enough to refactor from (see §7).
- **Cowardice and Marked by Death.** Persistent status that follows a character between days is good RPG texture. It just needs to matter more.

---

## 3. Diagnosis in detail

### 3.1 The economy never bites 📊

| Strategy | Median days | Endings | Clicks/fight (median) |
|---|---|---|---|
| *Miser*: never recruit, heal when hurt | 7 | 100% wipe | 12 |
| *Recruiter*: fill to 4 ASAP | 5 | 100% wipe | 26 |
| *Careful*: recruit/heal only with 2 days of wages banked | 6 | 100% wipe | 21 |

Why:
- **Day 1 is already lethal.** 📊 A fresh 2-person party beats 2–3 goblins only **87%** of the time, and the winners finish with a median of **66% HP left**.
- **HP never recovers on its own.** HP only comes back through the cleric (once a day) or a level-up, so each day's fight eats into a shrinking pool.
- **Income comfortably beats wages.** Day 1 brings in ~55 gold (45 commission + ~10 loot) against ~22 in wages, so you're always solvent. The 3-day mutiny timer only starts when `gold < totalWages`, and that never happens before you die.
- **Recruiting makes it worse.** More members means more enemies (`partyCount..partyCount*2`), longer fights and an XP split across more people.

**Implication:** the systems you designed for (wages, theft, mutiny) are effectively dead code in practice. Fix combat attrition and progression first, then put the economy in charge of difficulty.

### 3.2 Levelling inverts the reward loop

`PTSoul.LevelUp()`:
- `atrPoints++`, but points can only be spent through `On_4`, which is gated behind `debugMode`.
- `RecalculateStats()` runs on unchanged attributes, so there's **no stat gain**. Worse, it **removes** the +3 party defense bonus added in `Start()`.
- `dailywages *= 1.25`, compounding.
- `PickEnemyType(GetAveragePartyLevel())` puts Hoblins in at level 4, Goblin Lords at 7, and so on.

So the optimal play is to avoid XP. That's the opposite of what an RPG promises. The README's "+10 max HP per level" and "+1 atk/def every 2 levels" were never implemented.

### 3.3 Combat is a click-through

There's one meaningful verb (Attack), with random targeting and no positioning, abilities or defend. Run Away is a per-member roll that costs 10% of your gold *per attempt*. The player is the accountant, yet the combat UI asks them to *be* the fighters. That's a theme mismatch as well as a pacing problem.

### 3.4 Theft is a tax, not a mechanic

There's a 10% (25% if broke) nightly chance to lose a slice of your gold. You aren't told who did it, you get no warning signs, and you have no response. An accountant fantasy *wants* you to notice discrepancies, investigate and confront people.

### 3.5 Content and replay surface

There are 4 friendly races, 6 enemy tiers ordered strictly by level, one town, one encounter type and a 15% prefix chance with a single stat modifier. Two runs feel identical by day 3, and nothing persists between runs.

### 3.6 Bugs and doc drift that affect design

| Where | Issue | Effect |
|---|---|---|
| `PTManager.cs` `DeadCharacterData` + `PTSoul.Start()` | `DeadCharacterData` stores derived stats, not **attributes**. `PTSoul.Start()` runs the frame after `ApplyTo()` and calls `RecalculateStats()` from the prefab's default attributes, which overwrites the restored stats and the 10% penalty. | Resurrected heroes come back as a generic prefab, not themselves. |
| `PTSoul.RecalculateStats()` | Resets `defense`, which drops the `Start()` +3 party bonus on every level-up or attribute change. | Levelling makes members squishier. |
| `PTSoul.LevelUp()` | `Random.Range(currentHP + 10, maxHP)`: the max is exclusive, so a level-up never fully heals, and when `currentHP + 10 >= maxHP` the arguments are equal or inverted. | Unpredictable HP after a level-up. Use `Mathf.Min(currentHP + 10, maxHP)` or a defined % heal. |
| `PTManager.cs` theft constants | `THEFT_PERCENT_BROKE` (0.1) < `THEFT_PERCENT_NORMAL` (0.2), but the comment in `OnSleep` says "larger cut during the broke period". | Inverted against the stated intent. |
| `PTManager.Combat.cs` `OnEncounter` | `Random.Range(int, int)` has an exclusive max, so a 2-member party on day 1 gets 2–3 enemies (the README says 2–4), and a 1-member party always gets exactly 1. | Documentation is off and the scaling is flatter than intended. |
| `PTManager.Combat.cs` `ChangeXP` | XP is split evenly across the party. `killCredit` is written but never read, though the README says the killer gets all the XP. | Doc drift. Decide which rule you want. |
| `PTSoulGen.SpawnEnemy` + `PTSoulTypeData.ApplyAttributes` | Enemy level is clamped to ≤ party level, but per-level bonuses only apply above `levelRange.y`. | The enemy `bonus*PerLevel` fields are dead data. |
| `PTManager.cs` | `deadCharacterNames` is written but never read, and the README's "Mysterious Stranger" recruit doesn't exist. | Doc drift. |
| Input | All actions are keyboard-only, with a text legend. | A blocker for itch: players expect clickable UI and, ideally, a browser build. |

---

## 4. Design direction: "The Ledger is the game"

### Pillars
1. **Every coin is a decision.** Gold is HP, XP, loyalty and score. If an action doesn't involve money, it should be quick or automatic.
2. **Your employees are people.** They have greed, loyalty, grudges and friendships. Mutiny and theft come out of those relationships, not out of a dice timer.
3. **Short, legible, winnable runs.** About 30 minutes with a clear goal and visible escalation. A loss should leave you thinking "I know what I'd do differently."

### Run structure: the Queen's Audit (win condition + arc)
- A run lasts **4 weeks (28 days)**. Every 7th day the Queen's auditor arrives and demands **tribute** that escalates each week, for example 100 / 200 / 350 / 500 gold.
- If you can't pay, the auditor takes your best-paid hero as collateral (soft fail) or executes you (hard fail, at higher difficulty).
- On day 28 you face an optional **Goblin King** contract. Winning it clears your name (best ending). Skipping it and just paying the final audit earns a "pardoned" ending.
- **Score:** tribute paid + gold kept + survivors − deaths. Show it on a "Final Ledger" screen, which is the obvious screenshot moment for the itch page.

That gives the economy fixed deadlines that bite, and makes the core tension *spend on the party (survive) vs. bank for the Queen (win)*.

---

## 5. System proposals (prioritised)

### 5.1 Hiring Board & Contracts — *the accountant's main tool* ⭐
- The tavern shows **3 visible candidates** each day (race, stats, traits, asking wage) and refreshes nightly. This replaces the blind `F` recruit.
- Each hire picks a **contract type**:
  - *Day wage*: what you have now.
  - *Loot share*: no wage, but they take X% of combat gold, so they're greedy when fights go well.
  - *Signing bonus*: a big upfront payment, then a low wage.
- **Fire** a member: pay severance or skip it (−loyalty for everyone who watched).
- **Individual payroll** at night: choose who gets paid. Partial pay replaces today's all-or-nothing `gold < totalWages` check, and who you stiff becomes a decision.

### 5.2 Loyalty & Greed — *theft and mutiny as emergent systems* ⭐
Each member has **Loyalty** (0–100, visible) and **Greed** (a hidden trait, hinted at by flavour text).

| Event | Loyalty |
|---|---|
| Paid on time | +5 |
| Unpaid | −20 |
| Friend died in combat | −10 |
| Got the killing blow / a bonus | +5 to +10 |
| Fired someone in front of them | −5 |
| Blessed by the cleric | +10 |

- **Theft** becomes a per-member nightly roll: `greed × (1 − loyalty/100)`. Stolen gold shows up as a **ledger discrepancy** the next morning ("Expected 212g, counted 187g").
- **Audit** action (once per day): pick a suspect and roll against their Sense to catch them. If you're right, you can *dock their pay*, *fire* them or *forgive* them (+loyalty). If you're wrong, the accused loses loyalty.
- **Mutiny** happens when the party's average loyalty drops below 20, or when 2+ members are below 10. It plays out as a night event, and a loyal member may warn you the night before. Keep "3 unpaid days" only as a hard backstop.

This turns your best idea into the centrepiece and gives the player detective and HR fantasy without adding combat complexity.

### 5.3 Progression that pays off (fixes §3.2)
- On level-up: +1 to the race's preferred attribute (auto-allocated from race affinity, so no extra UI needed), +10 max HP, and a 30% heal.
- The wage rise becomes a **negotiation event**: "Dran wants a raise (15 → 19g)."
  - *Accept*: +loyalty.
  - *Refuse*: −15 loyalty, and Greedy characters may steal.
  - *Counter*: Sense-vs-Sense roll for a smaller raise.

This makes the trade-off explicit and player-owned: **power costs money, and you decide how much.**

### 5.4 Combat: watch your investment, intervene with gold (fixes §3.3)
The accountant shouldn't play the fighters. Make combat **auto-resolved and fast** (about 20–40 seconds, with 1×/2×/skip speed), and give the player **gold-spending interventions** each round:

| Intervention | Cost | Effect |
|---|---|---|
| **Hazard pay** | 5g × member | +25% damage this round, +loyalty |
| **Bribe** | enemy's gold reward × 2 | One enemy flees (you lose its loot) |
| **Potion toss** | 15g | Heal the lowest-HP member by 40 |
| **Sound the retreat** | 10% of gold | The whole party flees together; cowardice only for members with loyalty below 50 |
| **Order a target** | free, 1 per round | Focus-fire one enemy |

Before the fight, in town, set a **stance** (Aggressive / Balanced / Cautious) that biases targeting and defending. Combat stays readable, the clicking disappears, and *every decision in combat is a financial one*, which is on-theme.

Balance targets: ≤ 6 decisions and ≤ 45 seconds per fight. Let HP partially regenerate overnight (+20%) so that attrition shows up as cleric costs, not as surprise wipes.

### 5.5 Job Board (replaces the single `E` encounter)
Offer **2–3 contracts per day**, each with visible risk and reward:
- *Goblin nest*: 3–5 goblins, 60g.
- *Escort the tax wagon*: low fight chance, 30g, +loyalty.
- *Hoblin war-camp*: elite, 150g plus a relic, with a "may draw Lord" tag.
- *Rest day*: no fight, +40% HP, but wages are still due.

A job is just a ScriptableObject: enemy pool, count range, payout and tags. That's huge variety for very little code, and it gives you difficulty curves you control instead of the current `daysElapsed` formula.

### 5.6 Traits (extend the prefix system)
Replace the single stat prefix with **1–2 traits per recruit**, defined as a `PTTraitData` ScriptableObject with a few hooks (`OnPayday`, `OnKill`, `OnNight`, `OnAllyDeath`, `ModifyWage`). Ship 15–20:

> **Light-fingered** (steals often, but loot +20%) · **Loyal** (loyalty decays half as fast) · **Drunkard** (−Sense the morning after payday) · **Haggler** (cleric costs −25%) · **Pious** (bless gives double loyalty) · **Bloodthirsty** (+dmg; demands a bonus after a no-kill fight) · **Veteran** (starts level 3, wants a high wage) · **Coward** (starts Cowardly, cheap) · **Gambler** (wage randomised nightly) · **Union Rep** (if they're unpaid, everyone loses loyalty) · **Nephew of the Queen** (can't be fired; audits are lenient while they live)

Traits produce emergent stories ("the Light-fingered Halfling was my best fighter, so I let it slide…"), and that's the replayability itch players talk about.

### 5.7 Night events (the cheapest replayability per dev-hour)
A deck of 30–40 **event ScriptableObjects**. Each has text, 2–3 choices and gold/loyalty/HP outcomes, and some are gated by traits or day. One fires most nights. Examples:
- A tavern brawl.
- The cleric's going-out-of-business sale.
- A member asks for an advance for a sick mother.
- A goblin envoy offers a bribe to skip the nest.
- A forged-receipts opportunity: +gold, and the next audit may catch it.
- The rival accountant who framed you shows up.

### 5.8 Meta-progression (light, appropriate for itch)
- **Accusations** (starting archetypes, unlocked by wins or milestones):
  - *Embezzler*: +100g, but the auditor wants 20% more.
  - *Scapegoat*: start with a Loyal veteran.
  - *Bookkeeper*: theft discrepancies show the culprit's initial.
- **Audit Levels 1–10** (Ascension-style difficulty modifiers), stored in `PlayerPrefs`.
- **Daily seed run** with a shareable seed string.
- Unlock races and traits into the pool after first-time achievements, for example "Win with no deaths" unlocks Dwarven *Union Rep*.

---

## 6. itch.io release checklist

- [ ] **Clickable UI** for every action. Keep the hotkeys as shortcuts and show the costs on the buttons.
- [ ] **WebGL build.** Most itch players won't download a jam-scale game. Coroutine combat is fine, and System.Reflection isn't needed (the `using` in `PTManager.Combat.cs` is unused).
- [ ] Title screen, a 3-slide "how you got here" intro and a skippable guided first day.
- [ ] Settings: master/music/SFX volume, combat speed and text size.
- [ ] A Final Ledger run-summary screen: score, cause of death, best earner, biggest thief.
- [ ] Remove or gate the debug key `4`. Make `Escape` open a pause menu instead of instantly reloading the scene.
- [ ] itch page assets: a 3–5 second GIF of a theft-discrepancy reveal and a mutiny, 5 screenshots, and a one-line hook ("Hire heroes. Pay taxes. Get robbed.").
- [ ] An in-game feedback link and a version number on the title screen.

---

## 7. Architecture changes that enable the above

Do these alongside Phase 0. They're small now and expensive later.

1. **A `GameState` enum** (Town, Combat, Night, Event, Audit, GameOver) and a small state machine, replacing the `canSleep`, `hasFought`, `hasHealed`, `hasBeenBlessed` and `gameOver` booleans. New phases (night events, audits) then slot in cleanly.
2. **Split data from view.** Move stats into a plain serializable `SoulData` C# class and leave a thin `PTSoul` MonoBehaviour for the view and UI. That fixes the whole class of "`Start()` overwrote my data" bugs (resurrection, the defense bonus) and lets you run balance sims and EditMode tests headless.
3. **A `PTBalanceConfig` ScriptableObject** in place of the `const` block in `PTManager.cs`, so difficulty levels and Accusations become config swaps.
4. **Seeded `System.Random`** passed through the generators, for daily seeds and reproducible bug reports.
5. **Events, not direct calls.** Extend the existing `OnGoldChanged` pattern (`OnPayday`, `OnMemberDied`, `OnTheft`) so the log, UI and traits all subscribe instead of being called from `PTManager`.
6. **Port the balance sim** in `Tools/BalanceSim/` to an EditMode test once `SoulData` exists, so balance regressions get caught automatically.

---

## 8. Roadmap

| Phase | Scope | Est. | Exit criteria |
|---|---|---|---|
| **0 · Stabilise** ✅ | Fix the §3.6 bugs. Level-ups grant stats. Overnight regen. `GameState` + `SoulData` split. Clickable UI. Update the README. | 1 wk | 📊 Median run > 12 days. Day-1 wipe < 3%. |
| **1 · The Ledger (vertical slice)** ✅ | Hiring board, fire, individual payroll, Loyalty/Greed theft + Audit, Queen's tribute + 28-day win, Final Ledger screen. | 2–3 wks | Playtesters can explain *why* they lost. 📊 Endings: ≥ 50% economic (tribute/mutiny), ≤ 35% wipe. |
| **2 · Combat rework** | Auto-combat + speed control, gold interventions, stances, job board (6–8 jobs). | 2 wks | Fight ≤ 45 s. ≤ 6 decisions per fight. |
| **3 · Variety** | 15–20 traits, 30+ night events, wage negotiation, 3 Accusations, Audit levels 1–5. | 2 wks | Two runs on different seeds feel different by day 3. |
| **4 · Ship** | WebGL, title/settings/tutorial, audio pass, itch page, a closed playtest of 10+ people, then launch. | 1–2 wks | Average session ≥ 25 min. ≥ 30% of testers start a second run. |

### Phase 0 status (v0.5)

| Item | Status |
|---|---|
| §3.6 bugs | ✅ Resurrection keeps attributes and a lasting penalty (`PTSoul.statMultiplier`); `Start()` no longer overwrites spawned souls (`FinishSetup()`); defense bonus survives recalculation; level-up heal is deterministic; theft cut fixed; spawn range inclusive and documented; XP split documented, dead kill-credit code removed; enemy per-level bonuses apply after the level cap; unused `deadCharacterNames` and `System.Reflection` removed. Also fixed: recruits levelled to the party average started with negative XP. |
| Level-ups grant stats | ✅ +1 race affinity attribute (`PTSoulTypeData.levelUpAttribute`), +10 max HP, 30% heal |
| Overnight regen | ✅ 25% max HP when wages are paid, none when unpaid |
| Encounter scaling | ✅ party × 1.5 max (was × 2), 3 grace days. Recruiting is now the strongest strategy instead of the weakest |
| `GameState` | ✅ Town / Combat / GameOver replaces `canSleep` / `gameOver` |
| `SoulData` split | ⏸️ Deferred. Moving `PTSoul`'s fields into a nested class changes how every soul prefab is serialized and needs migrating in the editor. The bug class it was meant to fix ("`Start()` overwrote my data") is fixed directly with `FinishSetup()` for now |
| Clickable UI | ✅ `PTActionBar`, built at runtime with costs on every button; hotkeys still work |
| README | ✅ Updated to v0.5 |
| Exit criteria | ✅ 📊 Median 13–14 days for strategies that recruit (10 for one that never recruits); day-1 wipes 0.4–2.4% |
| Verification | `Tools/HeadlessTests` compiles all scripts against Unity API stubs and runs 25 behaviour checks. **Not yet run in the Unity editor**: needs a play-test pass |

### Phase 1 status (v0.6)

| Item | Status |
|---|---|
| Queen's tribute + 28-day win | ✅ 200/350/500/700 at the end of every 7th day, taken before wages. First miss: gold and best-paid hero seized. Second miss (or missing the last one): hanged. Paying day 28: pardoned |
| Individual payroll | ✅ Per-member pay toggles in the Ledger view; paid in order while gold lasts |
| Loyalty & Greed theft | ✅ Visible loyalty, hidden greed with a hint, per-member nightly theft, morning ledger discrepancy |
| Audit + confrontation | ✅ Once a day; catch chance from Sense; dock pay / forgive / fire; innocent suspects resent it |
| Mutiny & desertion | ✅ Mutiny below 20 average loyalty or two members below 10; desertion at 0 loyalty; warnings in the log. Replaces the old flat 3-day rule |
| Hiring board | ✅ 3 visible candidates each morning (stats, wage, greed hint), signing fee of 2 days' wages |
| Fire | ✅ With confirmation; one day's wage severance, or −10 loyalty for the others if you can't pay |
| Final Ledger screen | ✅ Outcome, score, totals, per-hire books, biggest thief, best earner |
| Contract types (loot share, signing bonus) | ⏸️ Moved to Phase 3. They pair naturally with traits, and day-wage contracts already carry the core tension |
| Goblin King contract on day 28 | ⏸️ Moved to Phase 2's job board, where contracts live |
| Economy re-tune | Required: compounding wages made every tribute schedule unwinnable. Raises are now linear (+15% of base per level), XP grows with √level, commission +25g/week, +15 HP per level, escalation +1 enemy per week. See README v0.6 |
| Exit criteria | ✅ 📊 *Accountant* play: 54% economic endings, 20% slain, 26% pardoned. *Naive*: 78% economic, 8% slain, 14% pardoned. Skill now beats spending freely almost 2:1 |
| Verification | `Tools/HeadlessTests`: 54 behaviour checks, including a full night sequence. **Not yet run in the Unity editor** |

Open question for playtests: scripted players almost never trigger mutiny (3%), because they always pay when they can. Watch whether humans deliberately stiff people, and whether the loyalty warnings come early enough to read.

**If time is short:** Phase 0 plus the Queen's tribute, Loyalty/Greed theft, the hiring board, and night events (as plain text, no new art) are enough for a strong jam-scale itch release. The theme is carried by systems, not content.

---

## 9. Balance targets to tune toward (use the sim)

| Metric | v0.4 📊 | Target |
|---|---|---|
| Median run length | 5–7 days | 18–28 days (≈ 30 min) |
| Runs ending by combat wipe | 100% | 25–35% |
| Runs ending by mutiny / tribute | 0% | 50–60% |
| Runs won (Audit Level 1) | 0% (no win state) | 25–35% for a new player |
| Clicks / decisions per fight | 12–26 clicks, ~0 decisions | ≤ 6 meaningful decisions |
| Wages as a share of income by week 3 | n/a (no one reaches it) | 55–70% |
