// Headless behaviour checks for the core Party Taxes rules. Run: dotnet run --project Tools/HeadlessTests
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PartyTaxes;

static class Program
{
    static int failures = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

    static PTSoul MakeSoul(int might, int agi, int con, int sense, int luck, bool adversary, SoulAttribute affinity, int wages = 20)
    {
        var s = new PTSoul { Name = "Test the Human", Type = "Human", level = 1, atrMight = might, atrAgility = agi, atrConstitution = con, atrSense = sense, atrLuck = luck, isAdversary = adversary, levelUpAttribute = affinity, dailywages = wages };
        s.FinishSetup();
        return s;
    }

    static int Main()
    {
        // 1. Derived stats and the party defense bonus
        var s = MakeSoul(3, 2, 4, 2, 2, false, SoulAttribute.Might);
        Check(s.maxHP == 100 && s.currentHP == 100, $"party soul HP = 60 + 10*Con (got {s.maxHP})");
        Check(s.defense == 11, $"party soul defense = 2*Con + 3 (got {s.defense})");
        var e = MakeSoul(3, 2, 4, 2, 2, true, SoulAttribute.Might);
        Check(e.defense == 8, $"enemy defense has no party bonus (got {e.defense})");

        // 2. Level up grants power and keeps the defense bonus
        s.currentHP = 50;
        s.GainXP(100);
        Check(s.level == 2 && s.atrMight == 4, $"level up: +1 affinity (Might {s.atrMight}, level {s.level})");
        Check(s.maxHP == 115, $"level up: +15 max HP (got {s.maxHP})");
        Check(s.attack == 16, $"level up: attack follows Might (got {s.attack})");
        Check(s.defense == 11, $"level up keeps the +3 defense bonus (got {s.defense})");
        Check(s.currentHP == 50 + 34, $"level up heals 30% of max HP (got {s.currentHP})");
        Check(s.dailywages == 23, $"level up raises wages by 15% of base wage (got {s.dailywages})");
        Check(s.currentXp == 0, $"overflow XP carried correctly (got {s.currentXp})");

        // 3. Levelling a fresh recruit directly never leaves negative XP
        var r = MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Constitution);
        r.LevelUp(); r.LevelUp();
        Check(r.currentXp == 0 && r.level == 3, $"direct level ups keep XP >= 0 (xp {r.currentXp})");

        // 4. Start() must not overwrite a generated soul
        int hpBefore = s.currentHP, xpBefore = s.currentXp + 7;
        s.currentXp = xpBefore;
        s.Start();
        Check(s.currentHP == hpBefore && s.currentXp == xpBefore, "Start() leaves an initialized soul alone");
        var scenePlaced = new PTSoul { atrConstitution = 2, atrMight = 2, level = 1, currentXp = 99 };
        scenePlaced.Start();
        Check(scenePlaced.maxHP == 80 && scenePlaced.currentHP == 80 && scenePlaced.currentXp == 0, $"Start() still sets up hand-placed souls (HP {scenePlaced.maxHP})");

        // 5. Resurrection round trip keeps identity and applies a lasting 10% penalty
        s.GainXP(30);
        var snapshot = new DeadCharacterData(s, null);
        var body = new PTSoul { atrMight = 1, atrConstitution = 1, level = 1 };   // prefab defaults
        snapshot.ApplyTo(body);
        body.Start();                                                              // runs a frame later in Unity
        Check(body.atrMight == s.atrMight && body.atrConstitution == s.atrConstitution && body.level == s.level, "resurrected soul keeps its own attributes and level");
        Check(body.maxHP == (int)Math.Round(s.maxHP * 0.9f, MidpointRounding.ToEven), $"resurrected max HP is 90% (got {body.maxHP} from {s.maxHP})");
        Check(body.currentHP == body.maxHP && body.markedByDeath && !body.isCowardly && body.currentXp == s.currentXp && s.currentXp == 37, "resurrected at full HP, Marked by Death, fearless, XP kept");
        int penalisedAttack = body.attack;
        body.GainXP(1000);
        Check(body.statMultiplier < 0.91f && body.attack < body.atrMight * 4, $"penalty survives level ups (attack {body.attack}, might {body.atrMight})");

        // 6. Enemy type data: level is capped before per-level bonuses
        var goblin = new PTSoulTypeData { typeName = "Goblin", isAdversary = true, levelRange = new UnityEngine.Vector2Int(1, 3), mightRange = new UnityEngine.Vector2Int(2, 2), agilityRange = new UnityEngine.Vector2Int(2, 2), constitutionRange = new UnityEngine.Vector2Int(1, 1), senseRange = new UnityEngine.Vector2Int(1, 1), luckRange = new UnityEngine.Vector2Int(1, 1), bonusMightPerLevel = 1f, levelUpAttribute = SoulAttribute.Might };
        bool capped = true, bonusOk = true;
        for (int i = 0; i < 200; i++)
        {
            var g = new PTSoul(); goblin.ApplyAttributes(g, 1);
            capped &= g.level == 1; bonusOk &= g.atrMight == 2;
            var g3 = new PTSoul(); goblin.ApplyAttributes(g3, 99);
            bonusOk &= g3.atrMight == 2 + (g3.level - 1);
        }
        Check(capped, "enemy level never exceeds the party level");
        Check(bonusOk, "per-level bonus applies to levels above the type's minimum");

        // 7. Encounter sizes follow the tuned formula
        var mgr = new PTManager();
        for (int i = 0; i < 4; i++) mgr.partyMembers.Add(MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Might));
        var size = typeof(PTManager).GetMethod("GetEncounterSize", BindingFlags.Instance | BindingFlags.NonPublic);
        var pool = PTJob.DefaultPool();
        PTJob nest = pool[1], patrol = pool[0], raid = pool[3];
        (int min, int max) Range(PTJob job, int day) { mgr.daysElapsed = day; int lo = 99, hi = 0; for (int i = 0; i < 2000; i++) { int n = (int)size.Invoke(mgr, new object[] { job }); lo = Math.Min(lo, n); hi = Math.Max(hi, n); } return (lo, hi); }
        Check(Range(nest, 4) == (4, 5), $"goblin nest, party of 4: 4-5 enemies (got {Range(nest, 4)})");
        Check(Range(nest, 7) == (4, 5) && Range(nest, 8) == (4, 6) && Range(nest, 15) == (4, 7), $"escalation +1 each week from day 8 (got {Range(nest, 7)}, {Range(nest, 8)}, {Range(nest, 15)})");
        Check(Range(patrol, 15) == (2, 4) && Range(raid, 4) == (6, 7), $"job size multipliers: patrol 0.5-1x without escalation, raid 1.5-1.75x (got {Range(patrol, 15)}, {Range(raid, 4)})");

        // 8. State starts in town; costs exposed for the action bar
        Check(mgr.State == PTManager.GameState.Town, "game starts in Town state");
        Check(mgr.HealCost == 40 && mgr.BlessCost == 400, "heal/bless costs scale with party size");

        Phase1Tests();

        Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // ── Phase 1: the Ledger ─────────────────────────────────────────────
    static object Call(PTManager m, string method, params object[] args)
    {
        foreach (var mi in typeof(PTManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            if (mi.Name == method && mi.GetParameters().Length == args.Length) return mi.Invoke(m, args);
        throw new Exception("no method " + method);
    }

    static PTManager NewRun(params int[] wages)
    {
        var m = new PTManager { daysElapsed = 1, gold = 0 };
        foreach (int w in wages)
        {
            var soul = MakeSoul(3, 2, 3, 3, 2, false, SoulAttribute.Might, w);
            soul.Name = "Hero" + m.partyMembers.Count;
            m.partyMembers.Add(soul);
            Call(m, "Employ", soul, 60);
        }
        return m;
    }

    static void Phase1Tests()
    {
        Console.WriteLine("\n-- Phase 1 --");
        Check(PTSoul.XpToNextLevel(1) == 100 && PTSoul.XpToNextLevel(2) == 212 && PTSoul.XpToNextLevel(4) == 500,
              $"XP curve 50*(L+1)*sqrt(L) (got {PTSoul.XpToNextLevel(1)}, {PTSoul.XpToNextLevel(2)}, {PTSoul.XpToNextLevel(4)})");
        var w = MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Might, 20);
        w.LevelUp(); w.LevelUp();
        Check(w.dailywages == 26 && w.baseWage == 20, $"wages rise linearly: 20 -> 26 at level 3 (got {w.dailywages})");

        // Payroll: pay whoever is ticked while the gold lasts
        var m = NewRun(10, 15, 20);
        m.gold = 30;
        m.partyMembers[1].employment.payTonight = false;
        var paid = (HashSet<PTSoul>)Call(m, "RunPayroll");
        Check(paid.Count == 2 && m.gold == 0 && m.UnpaidLastNight == 1, $"payroll pays ticked members it can afford (paid {paid.Count}, gold {m.gold})");
        Check(m.partyMembers[0].employment.loyalty == 65 && m.partyMembers[1].employment.loyalty == 45, "paid +5 loyalty, unpaid -15");
        Check(m.TotalWagesPaid == 30 && m.partyMembers[2].employment.wagesReceived == 20, "wages recorded in the books");

        // Desertion and mutiny
        m = NewRun(10, 10, 10); m.gold = 100;
        m.partyMembers[0].employment.loyalty = 0;
        var deserter = m.partyMembers[0].employment;
        Call(m, "ResolveDesertions");
        Check(m.partyMembers.Count == 2 && deserter.status == EmploymentStatus.Deserted && m.gold == 90, $"a member at 0 loyalty deserts with 10% of the gold (gold {m.gold})");
        Check(!(bool)Call(m, "CheckMutiny") && m.State == PTManager.GameState.Town, "no mutiny while loyalty is healthy");
        foreach (var p in m.partyMembers) p.employment.loyalty = 15;
        Check((bool)Call(m, "CheckMutiny") && m.State == PTManager.GameState.GameOver && m.RunOutcome == "Mutiny", "average loyalty below 20 means mutiny");

        // Theft is tracked per member and shows up as missing gold
        m = NewRun(10, 10); m.partyMembers[0].employment.greed = 0.9f; m.partyMembers[0].employment.loyalty = 0;
        m.partyMembers[1].employment.greed = 0f;
        int nightsWithTheft = 0;
        for (int i = 0; i < 200; i++) { m.gold = 1000; Call(m, "RunNightlyTheft"); if (m.LastNightMissing > 0) nightsWithTheft++; }
        var thiefJob = m.partyMembers[0].employment;
        Check(nightsWithTheft > 90 && nightsWithTheft < 160, $"greedy, disloyal member steals ~63% of nights (got {nightsWithTheft}/200)");
        Check(m.partyMembers[1].employment.stolenTotal == 0 && thiefJob.stolenTotal == m.TotalStolen && thiefJob.stolenUnrecovered == thiefJob.stolenTotal, "theft is attributed to the thief only");

        // Audits: innocent members resent it; caught thieves can be docked
        m.gold = 0;
        var innocent = m.partyMembers[1];
        m.TryAudit(innocent);
        Check(innocent.employment.loyalty == 45 && m.hasAudited && m.PendingConfrontation == null, "auditing an innocent costs 15 loyalty");
        m.TryAudit(m.partyMembers[0]);
        Check(m.PendingConfrontation == null, "only one audit per day");
        int stolen = thiefJob.stolenUnrecovered, tries = 0;
        while (m.PendingConfrontation == null && tries++ < 50) { m.hasAudited = false; m.TryAudit(m.partyMembers[0]); }
        Check(m.PendingConfrontation == m.partyMembers[0], "a thief gets caught by an audit");
        m.ResolveConfrontation(PTManager.Confrontation.DockPay);
        Check(m.gold == stolen && thiefJob.stolenUnrecovered == 0 && m.TotalRecovered == stolen && thiefJob.loyalty == 0, $"docking pay recovers {stolen}g");

        // Firing: severance if you can afford it, resentment if you can't
        m = NewRun(10, 12, 14); m.gold = 50;
        var fired = m.partyMembers[1];
        m.TryFire(fired);
        Check(m.gold == 38 && m.partyMembers.Count == 2 && fired.employment.status == EmploymentStatus.Fired && m.partyMembers[0].employment.loyalty == 60, "firing pays one day's severance");
        m.gold = 0; m.TryFire(m.partyMembers[1]);
        Check(m.partyMembers[0].employment.loyalty == 50, "firing without severance costs the others 10 loyalty");

        // Tribute schedule, strikes, seizure, execution and victory
        m = NewRun(10, 30);
        int[] dueDay = new int[29], dueAmount = new int[29];
        foreach (int d in new[] { 1, 6, 7, 8, 14, 15, 28 }) { m.daysElapsed = d; dueDay[d] = m.NextTributeDay; dueAmount[d] = m.NextTributeAmount; }
        Check(dueDay[1] == 7 && dueDay[7] == 7 && dueDay[8] == 14 && dueDay[28] == 28, "tribute falls due every 7th day");
        Check(dueAmount[1] == 250 && dueAmount[8] == 450 && dueAmount[15] == 650 && dueAmount[28] == 900, "tribute schedule 250/450/650/900");
        m.daysElapsed = 7; m.gold = 300;
        Check((bool)Call(m, "CollectTribute") && m.gold == 50 && m.TotalTributePaid == 250, "paying tribute");
        m.daysElapsed = 14; m.gold = 100;
        var bestPaid = m.partyMembers[1];
        Check((bool)Call(m, "CollectTribute") && m.TributeStrikes == 1 && m.gold == 0 && bestPaid.employment.status == EmploymentStatus.Seized && m.partyMembers.Count == 1,
              "first missed tribute: gold and best-paid hero seized");
        m.daysElapsed = 21;
        Check(!(bool)Call(m, "CollectTribute") && m.State == PTManager.GameState.GameOver && m.RunOutcome == "Hanged", "second missed tribute: hanged");
        m = NewRun(10); m.daysElapsed = 28; m.gold = 1000;
        Check(!(bool)Call(m, "CollectTribute") && m.State == PTManager.GameState.Victory && m.RunOutcome == "Pardoned" && m.IsRunOver, "final tribute paid: pardoned");
        Check(m.RunScore == 900 + 100 + 25 + 250, $"score = tribute + gold + survivors + pardon bonus (got {m.RunScore})");
        m.TryFire(m.partyMembers[0]);
        Check(m.partyMembers.Count == 1, "no actions once the run is over");

        // Signing fees
        m = NewRun(10);
        var cand = MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Might, 12);
        Check(m.SigningFee(cand) == 24, "signing fee is two days' wages");
        m.partyMembers[0].isCowardly = true;
        Check(m.SigningFee(cand) == 36, "cowards in the party make hiring 50% pricier");

        // A full night: tribute first, then payroll, rest, theft and a new morning
        m = NewRun(10, 10);
        foreach (var p in m.partyMembers) { p.employment.greed = 0f; p.currentHP = 50; }
        m.daysElapsed = 7; m.gold = 265;
        m.TrySleep();
        Check(m.daysElapsed == 8 && m.TotalTributePaid == 250 && m.gold == 5 && m.UnpaidLastNight == 1, $"night: tribute paid before wages, one member left unpaid (gold {m.gold}, unpaid {m.UnpaidLastNight})");
        Check(m.partyMembers[0].currentHP > 50 && m.partyMembers[1].currentHP == 50, "only paid members rest overnight");
        m.daysElapsed = 9; m.partyMembers[0].employment.stolenUnrecovered = 5; m.partyMembers[0].atrSense = 1;
        for (int i = 0; i < 50 && m.PendingConfrontation == null; i++) { m.hasAudited = false; m.TryAudit(m.partyMembers[0]); }
        m.TrySleep();
        Check(m.daysElapsed == 9, "can't sleep with a caught thief waiting for a decision");

        // Death closes the file and keeps it for resurrection
        m = NewRun(10, 10);
        var dead = m.partyMembers[0];
        Call(m, "RemovePartyMember", dead, EmploymentStatus.Slain, "Slain by test");
        Check(dead.employment.timesDied == 1 && m.FallenCount == 1 && m.runLedger.Count == 2, "slain members stay in the run ledger and can be raised");

        Phase2Tests();
    }

    // ── Phase 2: jobs, auto-combat, stances, interventions ──────────────
    static void SetPrivate(PTManager m, string property, object value) =>
        typeof(PTManager).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(m, value);

    static PTSoul Enemy(string name, int might, int con, int gold = 5)
    {
        var e = MakeSoul(might, 2, con, 1, 1, true, SoulAttribute.Might);
        e.Name = name; e.goldReward = gold; e.accumulatedLifeXp = 10;
        return e;
    }

    static void Phase2Tests()
    {
        Console.WriteLine("\n-- Phase 2 --");
        var pool = PTJob.DefaultPool();

        // Job board
        var m = NewRun(10, 10);
        m.daysElapsed = 1; Call(m, "RefreshJobBoard");
        Check(m.JobBoard.Count == 2 && m.JobBoard.All(j => j.minDay <= 1), $"day 1 board only offers day-1 jobs (got {m.JobBoard.Count})");
        m.daysElapsed = 20; Call(m, "RefreshJobBoard");
        Check(m.JobBoard.Count == 3 && m.JobBoard.Distinct().Count() == 3, "later boards draw 3 distinct contracts");
        m.daysElapsed = 28; Call(m, "RefreshJobBoard");
        Check(m.JobBoard.Count == 4 && m.JobBoard[3].kind == JobKind.Boss, "the Goblin King appears on day 28");
        m.daysElapsed = 8;
        Check(m.JobPay(pool[1]) == 45 && m.JobPay(m.goblinKingJob) == 400, $"job pay grows weekly (nest day 8 = {m.JobPay(pool[1])})");

        // Quiet escort: paid without a fight
        m = NewRun(10, 10); m.daysElapsed = 3; m.gold = 0;
        var quiet = PTJob.DefaultPool()[2]; quiet.ambushChance = 0f;
        var board = (List<PTJob>)typeof(PTManager).GetField("jobBoard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m);
        board.Clear(); board.Add(quiet);
        m.TryTakeJob(0);
        Check(m.gold == m.JobPay(quiet) && m.hasFought && m.State == PTManager.GameState.Town && m.partyMembers[0].employment.loyalty == 65,
              "an unambushed escort pays out and pleases the party");
        m.TryRestDay();
        Check(m.gold == m.JobPay(quiet), "only one job per day");

        // Rest day
        m = NewRun(10, 10); foreach (var p in m.partyMembers) p.currentHP = 20;
        m.TryRestDay();
        Check(m.partyMembers[0].currentHP == 20 + 36 && m.hasFought, $"rest day heals 40% of max HP (got {m.partyMembers[0].currentHP})");

        // Stance targeting
        m = NewRun(10, 10);
        var weak = Enemy("Weak", 1, 1); var brute = Enemy("Brute", 8, 3);
        weak.currentHP = 5;
        m.adversaries.Add(brute); m.adversaries.Add(weak);
        m.stance = PTManager.Stance.Aggressive;
        Check(Call(m, "PickTarget") == weak, "Aggressive finishes the weakest enemy");
        m.stance = PTManager.Stance.Cautious;
        Check(Call(m, "PickTarget") == brute, "Cautious goes for the biggest hitter");

        // A full auto-battle (the stub runs the coroutine to completion)
        m = NewRun(10, 10, 10, 10); m.gold = 0; m.daysElapsed = 3;
        foreach (var p in m.partyMembers) { p.atrMight = 8; p.RecalculateStats(); }
        m.adversaries.Add(Enemy("Gob A", 1, 1, 4)); m.adversaries.Add(Enemy("Gob B", 1, 1, 6));
        SetPrivate(m, "ActiveJob", pool[1]);
        Call(m, "StartCombat");
        Check(m.State == PTManager.GameState.Town && m.adversaries.Count == 0 && m.gold == 10 + m.JobPay(pool[1]) && m.ActiveJob == null && m.CombatRound >= 1,
              $"auto-combat resolves to a win and pays loot + the job (gold {m.gold}, rounds {m.CombatRound})");

        // A hopeless fight ends the run
        m = NewRun(10); m.partyMembers[0].currentHP = 1;
        m.adversaries.Add(Enemy("Ogre", 30, 30));
        Call(m, "StartCombat");
        Check(m.State == PTManager.GameState.GameOver && m.RunOutcome == "Slain", "losing everyone in battle ends the run");

        // Interventions (coroutine paused so we can act mid-fight)
        UnityEngine.MonoBehaviour.RunCoroutines = false;
        m = NewRun(10, 10); m.gold = 200; m.partyMembers[0].currentHP = 10;
        var big = Enemy("Big", 6, 2, 10); var small = Enemy("Small", 1, 1, 2);
        m.adversaries.Add(big); m.adversaries.Add(small);
        SetPrivate(m, "ActiveJob", pool[1]);
        Call(m, "StartCombat");
        m.TryPotion();
        Check(m.partyMembers[0].currentHP == 50 && m.gold == 185 && m.PotionUsedThisRound, "potion heals the most hurt member by 40 for 15g");
        m.TryPotion();
        Check(m.gold == 185, "one potion per round");
        m.TryHazardPay();
        Check(m.gold == 175 && m.HazardPaidThisRound && m.partyMembers[1].employment.loyalty == 62, "hazard pay: 5g per member, +2 loyalty");
        Check(m.BribeTarget == big && m.BribeCost == 35, $"bribe targets the toughest enemy for 15 + 2x loot (got {m.BribeCost})");
        m.TryBribe();
        Check(m.adversaries.Count == 1 && m.gold == 140 && m.State == PTManager.GameState.Combat, "bribed enemy leaves without loot");
        m.TryFocusFire();
        Check(Call(m, "PickTarget") == small && m.FocusOrderedThisRound, "focus fire overrides the stance target");
        m.partyMembers[1].employment.loyalty = 30;
        m.TryRetreat();
        Check(m.State == PTManager.GameState.Town && m.gold == 126 && m.adversaries.Count == 0 && m.partyMembers[1].isCowardly && !m.partyMembers[0].isCowardly,
              $"retreat: 10% of gold, no pay, low-loyalty members branded cowards (gold {m.gold})");

        // The Goblin King: no retreat, no bribe
        m = NewRun(10, 10); m.gold = 100;
        var king = Enemy("Grubnak", 5, 5, 50); king.isBoss = true;
        m.adversaries.Add(king);
        SetPrivate(m, "ActiveJob", m.goblinKingJob);
        Call(m, "StartCombat");
        m.TryRetreat(); m.TryBribe();
        Check(m.State == PTManager.GameState.Combat && m.adversaries.Count == 1 && m.gold == 100 && !m.CanRetreat, "no retreat from (or bribing) the Goblin King");
        UnityEngine.MonoBehaviour.RunCoroutines = true;

        // Boss multipliers
        var b = Enemy("Boss", 4, 4); b.healthMultiplier = 14f; b.attackMultiplier = 3f; b.FinishSetup();
        Check(b.maxHP == 100 * 14 && b.attack == 48 && b.defense == 8, $"boss multipliers scale HP and attack, not defense (HP {b.maxHP}, ATK {b.attack})");

        // Vindicated ending
        m = NewRun(10); m.daysElapsed = 28; m.gold = 1000;
        SetPrivate(m, "SlewGoblinKing", true);
        Call(m, "CollectTribute");
        Check(m.State == PTManager.GameState.Victory && m.RunOutcome == "Vindicated" && m.RunScore == 900 + 100 + 25 + 250 + 500,
              $"slaying the King then paying the Queen: Vindicated (score {m.RunScore})");

        // Speed cycles Normal -> Fast -> Skip -> Normal
        m = NewRun(10);
        m.CycleCombatSpeed(); var s1 = m.Speed; m.CycleCombatSpeed(); var s2 = m.Speed; m.CycleCombatSpeed();
        Check(s1 == PTManager.CombatSpeed.Fast && s2 == PTManager.CombatSpeed.Skip && m.Speed == PTManager.CombatSpeed.Normal, "combat speed cycles 1x, 2x, skip");

        Phase3Tests();
    }

    // ── Phase 3: traits, contracts, raises, events, accusations, audit levels ──
    static PTManager NewRunWithTraits(params string[][] traitSets)
    {
        var m = new PTManager { daysElapsed = 1, gold = 0 };
        foreach (var traits in traitSets)
        {
            var soul = MakeSoul(3, 2, 3, 3, 2, false, SoulAttribute.Might, 10);
            soul.Name = "Hero" + m.partyMembers.Count;
            m.partyMembers.Add(soul);
            Call(m, "GiveTraits", soul, new List<string>(traits), true);
            Call(m, "Employ", soul, 60);
        }
        return m;
    }

    static void SetEvent(PTManager m, string id, PTSoul subject)
    {
        SetPrivate(m, "PendingEvent", PTContent.Events.First(e => e.id == id));
        SetPrivate(m, "EventSubject", subject);
    }

    static void Phase3Tests()
    {
        Console.WriteLine("\n-- Phase 3 --");
        Check(PTContent.Traits.Count == 20 && PTContent.Events.Count >= 30, $"content loads from JSON ({PTContent.Traits.Count} traits, {PTContent.Events.Count} events)");
        Check(PTContent.Trait("nephew").cannotBeFired && PTContent.Trait("coward").wagePercent == -30, "trait fields deserialize");

        // Rolling respects exclusions
        var roller = new PTManager();
        bool clash = false; int doubles = 0;
        for (int i = 0; i < 2000; i++)
        {
            var t = (List<string>)Call(roller, "RollTraits");
            if (t.Count == 2) { doubles++; var a = PTContent.Trait(t[0]); var b = PTContent.Trait(t[1]); clash |= a.id == b.id || a.Excludes(b.id) || b.Excludes(a.id); }
        }
        Check(!clash && doubles > 550 && doubles < 850, $"traits: 1 or 2 per recruit, never conflicting ({doubles}/2000 with two)");

        // Applying traits
        var m = NewRunWithTraits(new[] { "tough" }, new[] { "coward" }, new[] { "veteran" });
        var tough = m.partyMembers[0]; var coward = m.partyMembers[1]; var vet = m.partyMembers[2];
        Check(tough.atrConstitution == 5 && tough.dailywages == 11, $"Tough: +2 Con, +10% wage (Con {tough.atrConstitution}, wage {tough.dailywages})");
        Check(coward.isCowardly && coward.dailywages == 7, "Coward: arrives Cowardly, 30% cheaper");
        Check((int)typeof(PTManager).GetMethod("TraitLevelBonus", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { vet }) == 2, "Veteran arrives 2 levels up");

        // Payroll hooks
        m = NewRunWithTraits(new[] { "mercenary" }, new[] { "loyal" }, new[] { "union_rep" }, new[] { "lucky" });
        m.gold = 1000;
        Call(m, "RunPayroll");
        Check(m.partyMembers[0].employment.loyalty == 60 && m.partyMembers[1].employment.loyalty == 65, "Mercenary gains nothing from pay; others +5");
        m.gold = 0;
        foreach (var p in m.partyMembers) p.employment.loyalty = 60;
        Call(m, "RunPayroll");
        Check(m.partyMembers[1].employment.loyalty == 47, $"Loyal loses half when unpaid: -8, plus -5 from the unpaid Union Rep (got {m.partyMembers[1].employment.loyalty})");
        Check(m.partyMembers[3].employment.loyalty == 40 && m.partyMembers[2].employment.loyalty == 45, "Union Rep unpaid: everyone else loses 5 more");

        // Contracts: fees, wages, loot share
        m = NewRun(10); m.gold = 1000;
        var cand = MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Might, 12);
        Check(m.SigningFee(cand, ContractType.DayWage) == 24 && m.SigningFee(cand, ContractType.LootShare) == 12 && m.SigningFee(cand, ContractType.SigningBonus) == 60,
              "signing fees: wage 2 days, share 1 day, bonus 5 days");
        Check(m.ContractWage(cand, ContractType.SigningBonus) == 7 && m.ContractWage(cand, ContractType.LootShare) == 0, "signing bonus pays 60% wage; loot share no wage");
        SetPrivate(m, "AuditLevel", 4);
        Check(m.SigningFee(cand, ContractType.DayWage) == 36, "Audit Level 4: signing fees +50%");
        var sharer = m.partyMembers[0]; sharer.employment.contract = ContractType.LootShare; sharer.RecalculateWage();
        Check(sharer.dailywages == 0, "loot-share members draw no wage");
        m.gold = 0; m.daysElapsed = 1;
        Call(m, "CompleteJob", PTJob.DefaultPool()[1]);
        Check(m.gold == 30 - 5 && sharer.employment.tookCutToday && sharer.employment.wagesReceived == 5, $"loot share takes 16% of the job (gold {m.gold})");
        Call(m, "RunPayroll");
        Check(sharer.employment.loyalty == 65, "a sharer who got a cut is happy at payroll");
        sharer.employment.tookCutToday = false; Call(m, "RunPayroll");
        Check(sharer.employment.loyalty == 60, "a sharer with no cut today loses 5");

        // Raise negotiation
        m = NewRun(20); var asker = m.partyMembers[0];
        asker.GainXP(100);
        Check(asker.level == 2 && asker.dailywages == 20 && asker.employment.requestedWage == 23 && m.PendingRaise == asker && m.HasPendingDecision,
              "a level-up asks for a raise instead of taking it");
        m.ResolveRaise(PTManager.RaiseChoice.Accept);
        Check(asker.dailywages == 23 && asker.employment.loyalty == 65 && m.PendingRaise == null, "accepting pays the raise, +5 loyalty");
        asker.GainXP(1000); int before = asker.dailywages, wanted = asker.employment.requestedWage;
        m.ResolveRaise(PTManager.RaiseChoice.Refuse);
        Check(asker.dailywages == before && asker.employment.loyalty == 50, "refusing keeps the wage, -15 loyalty");
        asker.GainXP(2000); before = asker.dailywages; wanted = asker.employment.requestedWage; int loyaltyBefore = asker.employment.loyalty;
        m.ResolveRaise(PTManager.RaiseChoice.Counter);
        Check((asker.dailywages == (before + wanted) / 2 && asker.employment.loyalty == loyaltyBefore) ||
              (asker.dailywages == wanted && asker.employment.loyalty == loyaltyBefore - 10), "countering: half the raise, or the full raise and -10 loyalty");
        var recruit = MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Might, 20);
        Call(m, "LevelUpTo", recruit, 3, true);
        Check(recruit.dailywages == 26 && (recruit.employment == null || recruit.employment.requestedWage == 0), "recruits levelled on hire don't haggle");

        // Night events
        m = NewRun(10, 10); m.gold = 100; m.daysElapsed = 3;
        var subject = m.partyMembers[0];
        SetEvent(m, "tavern_brawl", subject);
        Check(m.HasPendingDecision && m.CanChooseEventOption(0), "an event waits for a decision");
        m.ResolveEvent(0);
        Check(m.gold == 75 && m.PendingEvent == null, "paying for the brawl costs 25 gold");
        SetEvent(m, "tavern_brawl", subject); m.ResolveEvent(1);
        Check(subject.employment.loyalty == 45, "docking the subject's pay costs them 15 loyalty");
        m.gold = 10; SetEvent(m, "tavern_brawl", subject);
        Check(!m.CanChooseEventOption(0), "a choice you can't afford is disabled");
        m.gold = 100; SetEvent(m, "rival_accountant", subject); m.ResolveEvent(0);
        Check(m.TributeAdjustment == -60 && m.gold == 60, "events can change the next tribute");
        SetEvent(m, "goblin_prisoner", subject); m.ResolveEvent(1);
        Check(m.NextJobPayPercent == 25 && m.JobPay(PTJob.DefaultPool()[1]) == 38, "interrogating the prisoner: next job pays 25% more");
        SetEvent(m, "poacher", subject); m.gold = 0; m.ResolveEvent(2);
        Check(!m.partyMembers.Contains(subject) && subject.employment.status == EmploymentStatus.Quit && m.gold == 20, "a poached member leaves (status Quit)");
        bool traitGateRespected = true;
        for (int i = 0; i < 300; i++)
        {
            var fresh = NewRun(10, 10); fresh.daysElapsed = 10;
            Call(fresh, "DrawNightEvent");
            if (fresh.PendingEvent != null && !string.IsNullOrEmpty(fresh.PendingEvent.requiresTrait)) traitGateRespected = false;
        }
        Check(traitGateRespected, "trait-gated events never appear without that trait in the party");

        // Trait combat and economy hooks
        m = NewRunWithTraits(new[] { "haggler" }, new[] { "nephew" });
        Check(m.HealCost == 15, $"Haggler: cleric 25% cheaper (heal {m.HealCost})");
        m.daysElapsed = 7;
        Check(m.NextTributeAmount == 225, $"Nephew: tribute 10% lower (got {m.NextTributeAmount})");
        Check(!m.CanBeFired(m.partyMembers[1]), "the Queen's nephew can't be fired");
        m.gold = 0; Call(m, "CollectTribute");
        Check(m.partyMembers.Contains(m.partyMembers.Last()) && m.partyMembers.Count == 1 && m.partyMembers[0].employment.HasTrait("nephew"),
              "the nephew can't be seized as collateral");
        m = NewRunWithTraits(new[] { "brawler" });
        m.partyMembers[0].employment.hungover = true;
        Check(Math.Abs((float)Call(m, "TraitDamageMultiplier", m.partyMembers[0]) - 0.7f) < 0.001f, "a hungover Drunkard fights at 70%");

        // Accusations and Audit Levels
        m = NewRun(10); m.daysElapsed = 7;
        SetPrivate(m, "CurrentAccusation", Accusation.Embezzler);
        Check(m.NextTributeAmount == 300, $"Embezzler: tribute +20% (got {m.NextTributeAmount})");
        SetPrivate(m, "CurrentAccusation", Accusation.Clerk); SetPrivate(m, "AuditLevel", 5);
        Check(m.NextTributeAmount == 290 && m.ExtraEnemies == 1 && Math.Abs(m.TheftMultiplier - 1.3f) < 0.001f, $"Audit Level 5 stacks the rules (tribute {m.NextTributeAmount})");
        var size = typeof(PTManager).GetMethod("GetEncounterSize", BindingFlags.Instance | BindingFlags.NonPublic);
        m.partyMembers.Add(MakeSoul(2, 2, 2, 2, 2, false, SoulAttribute.Might)); m.daysElapsed = 3;
        int lo = 99; for (int i = 0; i < 300; i++) lo = Math.Min(lo, (int)size.Invoke(m, new object[] { PTJob.DefaultPool()[1] }));
        Check(lo == 3, $"Audit Level 5: one more enemy per job (min {lo} for a party of 2)");
        SetPrivate(m, "AuditLevel", 3); m.gold = 0; m.daysElapsed = 28;
        int baseScore = 0 + 0 + m.partyMembers.Count * 25;
        Check(m.RunScore == (int)Math.Round(baseScore * 1.5f), $"Audit Level 3 scores x1.5 (got {m.RunScore})");

        var start = new PTManager { gold = 45 };
        start.BeginRun(Accusation.Embezzler, 2, 1234, true);
        Check(start.State == PTManager.GameState.Town && start.gold == 145 && start.daysElapsed == 1 && start.AuditLevel == 2 && start.RunSeed == 1234 && start.IsDailyRun,
              "BeginRun applies the accusation, level and seed");

        // Unlocks between runs
        UnityEngine.PlayerPrefs.DeleteAll();
        Check(PTRunRules.MaxAuditLevel == 1 && !PTRunRules.IsUnlocked(Accusation.Embezzler) && PTRunRules.IsUnlocked(Accusation.Clerk), "fresh profile: only the Clerk and Level 1");
        var unlocked = PTRunRules.RecordRun(true, 1, 28, 3, 900);
        Check(PTRunRules.MaxAuditLevel == 2 && PTRunRules.IsUnlocked(Accusation.Embezzler) && PTRunRules.IsUnlocked(Accusation.Scapegoat) && PTRunRules.IsUnlocked(Accusation.Bookkeeper),
              $"a win at Level 1 unlocks Level 2 and the accusations it earned ({string.Join("; ", unlocked)})");
        PTRunRules.RecordRun(true, 1, 28, 0, 100);
        Check(PTRunRules.MaxAuditLevel == 2 && PTRunRules.BestScore == 900, "winning an easier level doesn't skip ahead; best score kept");
        UnityEngine.PlayerPrefs.DeleteAll();
    }
}
