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
        Check(s.maxHP == 110, $"level up: +10 max HP (got {s.maxHP})");
        Check(s.attack == 16, $"level up: attack follows Might (got {s.attack})");
        Check(s.defense == 11, $"level up keeps the +3 defense bonus (got {s.defense})");
        Check(s.currentHP == 50 + 33, $"level up heals 30% of max HP (got {s.currentHP})");
        Check(s.dailywages == 25, $"level up raises wages 25% (got {s.dailywages})");
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
        Check(body.maxHP == (int)Math.Round(s.maxHP * 0.9, MidpointRounding.ToEven), $"resurrected max HP is 90% (got {body.maxHP} from {s.maxHP})");
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
        Check(Range(4) == (4, 6), $"day 4, party of 4: 4-6 enemies (got {Range(4)})");
        Check(Range(7) == (4, 7) && Range(10) == (4, 8), $"escalation +1 every 3 days from day 7 (got {Range(7)}, {Range(10)})");

        // 8. State starts in town; costs exposed for the action bar
        Check(mgr.State == PTManager.GameState.Town, "game starts in Town state");
        mgr.partyMembers[0].isCowardly = true;
        int baseCost = mgr.TotalDailyWages / 2;
        Check(mgr.RecruitCost == (int)Math.Round(baseCost * 1.5), $"recruit cost x1.5 with a coward (got {mgr.RecruitCost} from {baseCost})");
        Check(mgr.HealCost == 40 && mgr.BlessCost == 400, "heal/bless costs scale with party size");

        Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
