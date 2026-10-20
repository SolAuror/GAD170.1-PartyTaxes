// Headless behaviour checks for the core Party Taxes rules. Run: dotnet run --project Tools/HeadlessTests
using System;
using System.Collections.Generic;
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
        (int min, int max) Range(int day) { mgr.daysElapsed = day; int lo = 99, hi = 0; for (int i = 0; i < 2000; i++) { int n = (int)size.Invoke(mgr, null); lo = Math.Min(lo, n); hi = Math.Max(hi, n); } return (lo, hi); }
        Check(Range(1) == (4, 4) && Range(3) == (4, 4), "grace days: one enemy per party member");
        Check(Range(4) == (4, 5), $"day 4, party of 4: 4-5 enemies (got {Range(4)})");
        Check(Range(7) == (4, 5) && Range(8) == (4, 6) && Range(15) == (4, 7), $"escalation +1 each week from day 8 (got {Range(7)}, {Range(8)}, {Range(15)})");

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
        Check(dueAmount[1] == 200 && dueAmount[8] == 350 && dueAmount[15] == 500 && dueAmount[28] == 700, "tribute schedule 200/350/500/700");
        m.daysElapsed = 7; m.gold = 250;
        Check((bool)Call(m, "CollectTribute") && m.gold == 50 && m.TotalTributePaid == 200, "paying tribute");
        m.daysElapsed = 14; m.gold = 100;
        var bestPaid = m.partyMembers[1];
        Check((bool)Call(m, "CollectTribute") && m.TributeStrikes == 1 && m.gold == 0 && bestPaid.employment.status == EmploymentStatus.Seized && m.partyMembers.Count == 1,
              "first missed tribute: gold and best-paid hero seized");
        m.daysElapsed = 21;
        Check(!(bool)Call(m, "CollectTribute") && m.State == PTManager.GameState.GameOver && m.RunOutcome == "Hanged", "second missed tribute: hanged");
        m = NewRun(10); m.daysElapsed = 28; m.gold = 800;
        Check(!(bool)Call(m, "CollectTribute") && m.State == PTManager.GameState.Victory && m.RunOutcome == "Pardoned" && m.IsRunOver, "final tribute paid: pardoned");
        Check(m.RunScore == 700 + 100 + 25 + 250, $"score = tribute + gold + survivors + pardon bonus (got {m.RunScore})");
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
        m.daysElapsed = 7; m.gold = 215;
        m.TrySleep();
        Check(m.daysElapsed == 8 && m.TotalTributePaid == 200 && m.gold == 5 && m.UnpaidLastNight == 1, $"night: tribute paid before wages, one member left unpaid (gold {m.gold}, unpaid {m.UnpaidLastNight})");
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
    }
}
