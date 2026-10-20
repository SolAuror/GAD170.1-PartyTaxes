"""Monte Carlo model of Party Taxes combat + economy rules as implemented (v0.5 / Phase 0).
Mirrors PTManager/PTSoul/PTSoulGen constants, Prefab_Management.prefab overrides and
SoulType_*.asset ranges -- update this file when those change.

Usage:
  python3 Tools/BalanceSim/party_taxes_sim.py            # report for the current rules
  python3 Tools/BalanceSim/party_taxes_sim.py --sweep    # try alternative tuning values
"""
import math, random, statistics, sys
from collections import Counter

ATTRS = ("m", "a", "c", "se", "l")          # Might, Agility, Constitution, Sense, Luck
FRIENDLY = {  # might, agi, con, sense, luck, wage, levelUpAttribute
    "Human":    ((1,3),(1,4),(2,4),(1,4),(1,4),(5,20), "m"),   # no override -> global 5-20
    "Dwarf":    ((3,7),(1,4),(3,7),(1,4),(2,5),(5,10), "c"),
    "Elf":      ((1,4),(3,7),(1,4),(3,7),(2,6),(12,18), "se"),
    "Halfling": ((1,4),(3,6),(2,5),(2,5),(4,8),(7,12), "l"),
}
ENEMY = {  # might, agi, con, sense, luck, levelRange, gold, xp, per-level bonus (m,a,c,se,l), levelUpAttribute
    "Goblin":      ((1,2),(2,4),(1,1),(1,3),(1,2),(1,3),(2,6),(28,38),     (0.3,0.5,0.2,0.2,0),   "m"),
    "Hoblin":      ((2,3),(2,5),(2,3),(1,4),(1,3),(4,6),(7,15),(108,132),  (0.4,0.6,0.2,0.3,0.2), "m"),
    "Goblin Lord": ((3,4),(2,4),(3,4),(3,5),(3,5),(7,9),(16,26),(162,198), (0.5,0.7,0.1,0.4,0.2), "m"),
    "Orcling":     ((4,5),(2,3),(4,5),(1,3),(2,4),(10,12),(25,40),(270,330),(0.6,0.8,0.2,0.5,0.3),"c"),
    "Orc":         ((5,7),(1,2),(5,6),(2,4),(1,3),(13,15),(40,65),(390,480),(0.8,1,0.3,0.6,0.3),  "c"),
    "Orc Lord":    ((10,14),(2,4),(9,12),(3,5),(2,5),(16,20),(120,200),(800,1016),(1,1.2,0.5,0.5,0.4),"c"),
}

# Mirrors constants in PTManager.cs / PTSoul.cs. Override with --sweep to explore.
RULES = dict(
    START_GOLD=45, COMMISSION=45, WEEKLY=40, PREFIX_CHANCE=0.15,
    HEAL_AMT=50, HEAL_COST=10, MUTINY=3,
    PARTY_DEF_BONUS=3,
    HP_PER_LEVEL=10, LEVEL_UP_HEAL=0.3, WAGE_GROWTH=1.25,
    REST_HEAL_PAID=0.25, REST_HEAL_UNPAID=0.0,
    GRACE_DAYS=3,                       # days on which spawn count == party size
    SPAWN_MAX_MULT=1.5,                 # max spawn = ceil(party * mult) + escalation
    ESCALATION_START=7, ESCALATION_EVERY=3,
    THEFT=((0.1, 0.1), (0.25, 0.2)),    # (chance, percent) when paid / when broke
)

r = lambda lo_hi: random.randint(lo_hi[0], lo_hi[1])
R = dict(RULES)

class Soul:
    def __init__(s, attrs, adversary, affinity):
        s.m, s.a, s.c, s.se, s.l = attrs
        s.adv = adversary; s.aff = affinity; s.level = 1; s.xp = 0; s.wage = 0
        s.gold = 0; s.lifexp = 25; s.bonus_hp = 0
        if random.random() < R["PREFIX_CHANCE"]:
            k = random.choice(ATTRS)
            setattr(s, k, max(1, getattr(s, k) + random.choice([-2,-1,1,2,3])))
        s.hp = 0; s.recalc(); s.hp = s.maxhp
    def recalc(s):
        s.maxhp = 60 + s.c*10 + s.bonus_hp; s.atk = s.m*4
        s.df = s.c*2 + (0 if s.adv else R["PARTY_DEF_BONUS"])
        s.crit = min(s.l*3, 75); s.cm = 1.5 + 0.02*(s.l+s.m+s.se)
        s.hp = min(s.hp, s.maxhp)
    @property
    def alive(s): return s.hp > 0
    def next_xp(s): return (s.level+1)*50
    def deal(s): return round(s.atk*s.cm) if random.randrange(100) < s.crit else s.atk
    def take(s, dmg):
        d = max(1, round(dmg*(1 - s.df/(s.df+50)))); s.hp = max(0, s.hp-d)
    def level_up(s):
        s.level += 1; s.lifexp += s.xp
        setattr(s, s.aff, getattr(s, s.aff) + 1); s.bonus_hp += R["HP_PER_LEVEL"]
        s.recalc(); s.hp = min(s.maxhp, s.hp + round(s.maxhp*R["LEVEL_UP_HEAL"]))
        s.wage = round(s.wage*R["WAGE_GROWTH"])
    def gain(s, x):
        s.xp += x
        while s.xp >= s.next_xp():
            s.xp -= s.next_xp(); s.level_up()

def recruit(avg_level=0):
    f = random.choice(list(FRIENDLY.values()))
    s = Soul(tuple(r(x) for x in f[:5]), False, f[6]); s.wage = r(f[5])
    while s.level < avg_level: s.level_up()
    s.hp = s.maxhp
    return s

def enemy(plevel):
    cands = [v for v in ENEMY.values() if v[5][0] <= plevel <= v[5][1]]
    e = random.choice(cands) if cands else min(ENEMY.values(), key=lambda v: abs(v[5][1]-plevel))
    level = max(1, min(r(e[5]), plevel))
    above = max(0, level - e[5][0])
    attrs = tuple(r(e[i]) + round(e[8][i]*above) for i in range(5))
    s = Soul(attrs, True, e[9]); s.level = level
    s.gold = r(e[6]); s.lifexp = r(e[7]); return s

def spawn_count(pc, day):
    if day <= R["GRACE_DAYS"]: return pc
    e0, ev = R["ESCALATION_START"], R["ESCALATION_EVERY"]
    extra = 0 if day < e0 else (day-e0)//ev + 1
    return random.randint(pc, math.ceil(pc*R["SPAWN_MAX_MULT"]) + extra)   # inclusive, mirrors GetEncounterSize

def fight(party, foes, ledger):
    while foes and party:
        ps = sum(p.se for p in party)/len(party); es = sum(f.se for f in foes)/len(foes)
        for side in (("E","P") if es > ps else ("P","E")):
            if side == "P":
                for p in sorted(party, key=lambda x: -x.se):
                    if not foes: break
                    if not p.alive: continue
                    ledger["clicks"] += 1; t = random.choice(foes); t.take(p.deal())
                    if not t.alive:
                        foes.remove(t); ledger["gold"] += t.gold
                        for q in party: q.gain(t.lifexp // len(party))
            else:
                for f in sorted(foes, key=lambda x: -x.se):
                    if not party: break
                    t = random.choice(party); t.take(f.deal())
                    if not t.alive:
                        party.remove(t)
                        for g in foes: g.gain(t.lifexp // len(foes))
            if not foes or not party: break

def run(policy, max_days=40):
    party = [recruit(), recruit()]; gold = R["START_GOLD"]; day = 1; broke = 0
    out = dict(clicks=[], wages=[], levels=[])
    while day <= max_days:
        avg_lvl = sum(p.level for p in party)//len(party)
        wages = sum(p.wage for p in party)
        hc = R["HEAL_COST"]*len(party)
        if any(p.hp < p.maxhp*0.6 for p in party) and gold - hc >= (wages if policy == "careful" else 0):
            gold -= hc
            for p in party: p.hp = min(p.maxhp, p.hp + R["HEAL_AMT"])
        rc = wages//2
        if policy != "miser" and len(party) < 4 and gold - rc >= (wages*2 if policy == "careful" else 0):
            gold -= rc; party.append(recruit(avg_lvl))
        foes = [enemy(avg_lvl) for _ in range(spawn_count(len(party), day))]
        ledger = Counter(); fight(party, foes, ledger)
        out["clicks"].append(ledger["clicks"])
        if not party: return day, "wipe", out
        gold += ledger["gold"] + R["COMMISSION"]
        day += 1
        wages = sum(p.wage for p in party); out["wages"].append(wages)
        paid = gold >= wages
        if paid: gold -= wages; broke = 0
        else:
            broke += 1
            if broke >= R["MUTINY"]: return day, "mutiny", out
        rest = R["REST_HEAL_PAID"] if paid else R["REST_HEAL_UNPAID"]
        for p in party: p.hp = min(p.maxhp, p.hp + round(p.maxhp*rest))
        tc, tp = R["THEFT"][1 if broke else 0]
        if random.random() < tc and gold > 0: gold -= max(1, int(gold*tp))
        if day % 7 == 0: gold += R["WEEKLY"]
        out["levels"].append(sum(p.level for p in party)/len(party))
    return day, "survived", out

def report(n=2000, seed=1, quiet=False):
    random.seed(seed); rows = {}
    for pol in ("miser", "recruiter", "careful"):
        res = [run(pol) for _ in range(n)]
        days = sorted(d for d,_,_ in res); ends = Counter(e for _,e,_ in res)
        day1 = sum(1 for d,e,_ in res if e == "wipe" and d == 1)/n
        clicks = sorted(c for *_, o in res for c in o["clicks"])
        rows[pol] = dict(median=statistics.median(days), p10=days[n//10], p90=days[9*n//10],
                         day1=day1, ends=ends, clicks=statistics.median(clicks))
        if not quiet:
            print(f"\n== policy: {pol}")
            print(f" days survived  median {statistics.median(days)}  p10 {days[n//10]}  p90 {days[9*n//10]}")
            print(f" day-1 wipes    {day1:.1%}")
            print(f" endings        " + ", ".join(f"{k} {v/n:.0%}" for k,v in ends.most_common()))
            print(f" clicks/fight   median {statistics.median(clicks)}  p90 {clicks[9*len(clicks)//10]}")
            w = [o["wages"][13] for *_, o in res if len(o["wages"]) > 13]
            if w: print(f" wages day 15   median {statistics.median(w)}  (runs alive: {len(w)/n:.0%})")
    return rows

if __name__ == "__main__":
    if "--sweep" in sys.argv:
        grid = [dict(), dict(SPAWN_MAX_MULT=2.0), dict(SPAWN_MAX_MULT=1.25), dict(ESCALATION_EVERY=4),
                dict(ESCALATION_START=10), dict(GRACE_DAYS=2), dict(REST_HEAL_PAID=0.15), dict(WAGE_GROWTH=1.15)]
        for g in grid:
            R.clear(); R.update(RULES); R.update(g)
            rows = report(n=1000, quiet=True)
            line = " | ".join(f"{p}: med {v['median']:>4} d1 {v['day1']:.1%} wipe {v['ends']['wipe']/1000:.0%} mut {v['ends']['mutiny']/1000:.0%} ok {v['ends']['survived']/1000:.0%}" for p,v in rows.items())
            print(f"{str(g) or 'baseline':40} {line}")
    else:
        report()
