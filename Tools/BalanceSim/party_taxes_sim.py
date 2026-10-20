"""Monte Carlo model of Party Taxes v0.4 rules as implemented (not as documented).
# Monte Carlo model of Party Taxes v0.4 combat + economy. Usage: python3 Tools/BalanceSim/party_taxes_sim.py
# Mirrors constants in PTManager.cs, Prefab_Management.prefab overrides and SoulType_*.asset ranges; update both together.
Mirrors PTManager/PTSoul/PTSoulGen + SoulType assets + Prefab_Management overrides."""
import random, statistics, sys
from collections import Counter

FRIENDLY = {  # might, agi, con, sense, luck, wage
    "Human":    ((1,3),(1,4),(2,4),(1,4),(1,4),(5,20)),   # no override -> global 5-20
    "Dwarf":    ((3,7),(1,4),(3,7),(1,4),(2,5),(5,10)),
    "Elf":      ((1,4),(3,7),(1,4),(3,7),(2,6),(12,18)),
    "Halfling": ((1,4),(3,6),(2,5),(2,5),(4,8),(7,12)),
}
ENEMY = {  # might, agi, con, sense, luck, levelRange, gold, xp
    "Goblin":      ((1,2),(2,4),(1,1),(1,3),(1,2),(1,3),(2,6),(28,38)),
    "Hoblin":      ((2,3),(2,5),(2,3),(1,4),(1,3),(4,6),(7,15),(108,132)),
    "Goblin Lord": ((3,4),(2,4),(3,4),(3,5),(3,5),(7,9),(16,26),(162,198)),
    "Orcling":     ((4,5),(2,3),(4,5),(1,3),(2,4),(10,12),(25,40),(270,330)),
    "Orc":         ((5,7),(1,2),(5,6),(2,4),(1,3),(13,15),(40,65),(390,480)),
    "Orc Lord":    ((10,14),(2,4),(9,12),(3,5),(2,5),(16,20),(120,200),(800,1016)),
}
START_GOLD, COMMISSION, WEEKLY = 45, 45, 40
PREFIX_CHANCE = 0.15
HEAL_AMT, HEAL_COST, MUTINY = 50, 10, 3

r = lambda lo_hi: random.randint(lo_hi[0], lo_hi[1])

class Soul:
    def __init__(s, m, a, c, se, l, adversary):
        s.m, s.a, s.c, s.se, s.l = m, a, c, se, l
        s.adv = adversary; s.level = 1; s.xp = 0; s.wage = 0; s.gold = 0; s.lifexp = 25
        if random.random() < PREFIX_CHANCE:
            attr = random.choice("m a c se l".split())
            setattr(s, attr, max(1, getattr(s, attr) + random.choice([-2,-1,1,2,3])))
        s.recalc(); s.hp = s.maxhp
        if not adversary: s.df += 3          # PTSoul.Start bonus (lost on next recalc)
    def recalc(s):
        s.maxhp = 60 + s.c*10; s.atk = s.m*4; s.df = s.c*2
        s.crit = min(s.l*3, 75); s.cm = 1.5 + 0.02*(s.l+s.m+s.se)
        s.hp = min(getattr(s, "hp", s.maxhp), s.maxhp)
    @property
    def alive(s): return s.hp > 0
    def next_xp(s): return (s.level+1)*50
    def deal(s):
        return round(s.atk*s.cm) if random.randrange(100) < s.crit else s.atk
    def take(s, dmg):
        red = s.df/(s.df+50); d = max(1, round(dmg*(1-red))); s.hp = max(0, s.hp-d); return d
    def gain(s, x):
        s.xp += x
        while s.xp >= s.next_xp():
            s.level += 1; s.lifexp += s.xp; s.xp -= s.next_xp() - 0  # overflow uses old threshold
            s.recalc()                                              # attributes unchanged -> no gain
            lo = s.hp + 10; s.hp = min(s.maxhp, random.randint(min(lo, s.maxhp), max(lo, s.maxhp)))
            s.wage = round(s.wage*1.25)

def recruit(avg_level=None):
    t = random.choice(list(FRIENDLY)); f = FRIENDLY[t]
    s = Soul(r(f[0]), r(f[1]), r(f[2]), r(f[3]), r(f[4]), False); s.wage = r(f[5]); s.type = t
    if avg_level:
        while s.level < avg_level:   # LevelUpToPartyAverage -> LevelUp()
            s.level += 1; s.wage = round(s.wage*1.25)
    return s

def enemy(plevel):
    cands = [k for k,v in ENEMY.items() if v[5][0] <= plevel <= v[5][1]]
    k = random.choice(cands) if cands else min(ENEMY, key=lambda k: abs(ENEMY[k][5][1]-plevel))
    e = ENEMY[k]; s = Soul(r(e[0]), r(e[1]), r(e[2]), r(e[3]), r(e[4]), True)
    s.gold = r(e[6]); s.lifexp = r(e[7]); s.type = k; return s

def run(policy, max_days=60):
    party = [recruit(), recruit()]; gold = START_GOLD; day = 1; broke = 0
    stats = Counter(); clicks = []; wages_hist = []
    while day <= max_days:
        avg_lvl = sum(p.level for p in party)//len(party)
        wages = sum(p.wage for p in party)
        # --- town ---
        if any(p.hp < p.maxhp*0.6 for p in party) and gold >= HEAL_COST*len(party) + (wages if policy=="careful" else 0):
            gold -= HEAL_COST*len(party)
            for p in party: p.hp = min(p.maxhp, p.hp+HEAL_AMT)
        rc = wages//2
        if policy in ("recruiter","careful") and len(party) < 4 and gold - rc >= (wages*2 if policy=="careful" else 0):
            gold -= rc; party.append(recruit(avg_lvl))
        # --- combat ---
        pc = len(party); extra = 0 if day < 7 else (day-7)//3 + 1
        n = random.randrange(pc, max(pc+1, pc*2+extra))
        foes = [enemy(avg_lvl) for _ in range(n)]
        c = 0; rounds = 0
        while foes and party and rounds < 500:
            rounds += 1
            ps = sum(p.se for p in party)/len(party); es = sum(f.se for f in foes)/len(foes)
            order = ["E","P"] if es > ps else ["P","E"]
            for side in order:
                if side == "P":
                    for p in sorted(party, key=lambda x: -x.se):
                        if not foes or not p.alive: continue
                        c += 1; t = random.choice(foes); t.take(p.deal())
                        if not t.alive:
                            foes.remove(t); gold += t.gold
                            share = t.lifexp // len(party)
                            for q in party: q.gain(share)
                else:
                    for f in sorted(foes, key=lambda x: -x.se):
                        if not party: break
                        t = random.choice(party); t.take(f.deal())
                        if not t.alive:
                            party.remove(t); stats["deaths"] += 1
                            for g in foes: g.gain(t.lifexp//len(foes))
                if not foes or not party: break
        clicks.append(c)
        if not party: return day, "wipe", clicks, wages_hist, stats
        gold += COMMISSION
        # --- sleep ---
        day += 1
        wages = sum(p.wage for p in party); wages_hist.append(wages)
        if gold >= wages: gold -= wages; broke = 0
        else:
            broke += 1
            if broke >= MUTINY: return day, "mutiny", clicks, wages_hist, stats
        tc, tp = (0.25, 0.1) if broke else (0.1, 0.2)
        if random.random() < tc and gold > 0:
            stolen = max(1, int(gold*tp)); gold -= stolen; stats["stolen"] += stolen
        if day % 7 == 0: gold += WEEKLY
    return day, "survived", clicks, wages_hist, stats

if __name__ == "__main__":
    random.seed(1); N = 2000
    for pol in ("miser", "recruiter", "careful"):
        res = [run(pol) for _ in range(N)]
        days = [d for d,*_ in res]; ends = Counter(e for _,e,*_ in res)
        clicks = [c for x in res for c in x[2]]
        print(f"\n== policy: {pol}")
        print(f" days survived  median {statistics.median(days)}  p10 {sorted(days)[N//10]}  p90 {sorted(days)[9*N//10]}")
        print(f" endings        {dict(ends)}")
        print(f" clicks/fight   median {statistics.median(clicks)}  p90 {sorted(clicks)[9*len(clicks)//10]}")
        w5 = [x[3][4] for x in res if len(x[3]) > 4]; w20 = [x[3][19] for x in res if len(x[3]) > 19]
        if w5: print(f" wages day 6    median {statistics.median(w5)}")
        if w20: print(f" wages day 21   median {statistics.median(w20)}  (n={len(w20)})")
        print(f" gold stolen    mean {statistics.mean(x[4]['stolen'] for x in res):.0f}")
