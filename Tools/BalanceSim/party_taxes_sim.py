"""Monte Carlo model of Party Taxes combat + economy rules as implemented (v0.6 / Phase 1).
Mirrors PTManager/PTSoul/PTSoulGen constants, Prefab_Management.prefab overrides and
SoulType_*.asset ranges -- update this file when those change.

Usage:
  python3 Tools/BalanceSim/party_taxes_sim.py            # report for the current rules
  python3 Tools/BalanceSim/party_taxes_sim.py --sweep    # try alternative tuning values

Policies are simple scripted players: 'naive' spends freely and never audits; 'accountant'
keeps a full party, banks for the Queen in the second half of each week, audits after thefts
and rests when hurt; 'hoarder' never hires beyond a pair and banks everything.
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
    START_GOLD=45, COMMISSION=45, PREFIX_CHANCE=0.15,
    HEAL_AMT=50, HEAL_COST=10,
    PARTY_DEF_BONUS=3,
    HP_PER_LEVEL=15, LEVEL_UP_HEAL=0.3,
    WAGE_RAISE=0.15, WAGE_COMPOUND=False,  # per level: compound x(1+raise), or linear +raise x base wage
    XP_POW=0.5,                          # XP to next level = 50*(level+1) * level^XP_POW
    COMMISSION_PER_WEEK=25,              # commission grows each week of the run
    REST_HEAL_PAID=0.25,
    GRACE_DAYS=3,                       # days on which spawn count == party size
    SPAWN_MAX_MULT=1.25,                # max spawn = ceil(party * mult) + escalation
    ESCALATION_START=8, ESCALATION_EVERY=7,
    # --- Phase 1: the Ledger ---
    RUN_DAYS=28, TRIBUTE=(200, 350, 500, 700), STRIKES_TO_EXECUTION=2,
    CANDIDATES=3, SIGN_FEE_DAYS=2,      # signing fee = candidate wage x this
    LOYALTY_START=60,
    L_PAID=5, L_UNPAID=-15, L_ALLY_DIED=-10, L_KILL=3, L_WRONG_ACCUSE=-15, L_DOCKED=-10,
    GREED=(0.1, 0.9), THEFT_SCALE=0.7, THEFT_CUT=(0.05, 0.12), THEFT_LOYALTY_DIV=150,  # loyal staff still skim, just less
    DESERT_CUT=0.1, MUTINY_AVG=20, MUTINY_LOW=10,
    AUDIT_BASE=0.8, AUDIT_PER_SENSE=0.05, AUDIT_RANGE=(0.4, 0.9),
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
    def next_xp(s): return round((s.level+1)*50 * s.level**R["XP_POW"])
    def deal(s): return round(s.atk*s.cm) if random.randrange(100) < s.crit else s.atk
    def take(s, dmg):
        d = max(1, round(dmg*(1 - s.df/(s.df+50)))); s.hp = max(0, s.hp-d)
    def level_up(s):
        s.level += 1; s.lifexp += s.xp
        setattr(s, s.aff, getattr(s, s.aff) + 1); s.bonus_hp += R["HP_PER_LEVEL"]
        s.recalc(); s.hp = min(s.maxhp, s.hp + round(s.maxhp*R["LEVEL_UP_HEAL"]))
        s.wage = round(s.wage*(1+R["WAGE_RAISE"])) if R["WAGE_COMPOUND"] else round(s.base_wage*(1 + R["WAGE_RAISE"]*(s.level-1)))
    def gain(s, x):
        s.xp += x
        while s.xp >= s.next_xp():
            s.xp -= s.next_xp(); s.level_up()

def recruit(avg_level=0):
    f = random.choice(list(FRIENDLY.values()))
    s = Soul(tuple(r(x) for x in f[:5]), False, f[6]); s.wage = s.base_wage = r(f[5])
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


def hire(soul):
    soul.loyalty = R["LOYALTY_START"]; soul.greed = random.uniform(*R["GREED"]); soul.stash = 0
    return soul

def greed_hint(s):                      # what the player can see: "honest face" / "hard to read" / "eyes your purse"
    return 0.2 if s.greed < 0.35 else (0.8 if s.greed > 0.65 else 0.5)

def loyal(s, d): s.loyalty = max(0, min(100, s.loyalty + d))

def tribute_due(day):                   # tribute owed at the end of `day` (0 if not a tribute day)
    return R["TRIBUTE"][day//7 - 1] if day % 7 == 0 and day//7 <= len(R["TRIBUTE"]) else 0

def next_tribute(day):
    week = (day - 1)//7
    return R["TRIBUTE"][week] if week < len(R["TRIBUTE"]) else 0

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
                        foes.remove(t); ledger["gold"] += t.gold; loyal(p, R["L_KILL"])
                        for q in party: q.gain(t.lifexp // len(party))
            else:
                for f in sorted(foes, key=lambda x: -x.se):
                    if not party: break
                    t = random.choice(party); t.take(f.deal())
                    if not t.alive:
                        party.remove(t)
                        for q in party: loyal(q, R["L_ALLY_DIED"])
                        for g in foes: g.gain(t.lifexp // len(foes))
            if not foes or not party: break

def value(s):                           # how good a hire looks to a scripted player: raw fighting strength
    return s.atk * s.maxhp * (1 + s.df/50)

def run(policy):
    party = [hire(recruit()), hire(recruit())]; gold = R["START_GOLD"]; day = 1; strikes = 0
    out = Counter(); missing = 0
    while True:
        avg_lvl = sum(p.level for p in party)//len(party)
        wages = sum(p.wage for p in party)
        reserve = 0
        into_week = (day - 1) % 7 + 1
        if policy == "accountant":      # start banking for the Queen from day 4 of each week
            reserve = next_tribute(day) * max(0, into_week - 3) / 4 + wages
        elif policy == "hoarder":
            reserve = next_tribute(day) + wages
        # --- morning: audit ---
        if policy == "accountant" and missing > 0 and party:
            suspect = max(party, key=lambda q: greed_hint(q) * (100 - q.loyalty))
            if suspect.stash > 0:
                chance = min(R["AUDIT_RANGE"][1], max(R["AUDIT_RANGE"][0], R["AUDIT_BASE"] - (suspect.se - 3)*R["AUDIT_PER_SENSE"]))
                if random.random() < chance:
                    gold += suspect.stash; out["recovered"] += suspect.stash; suspect.stash = 0; loyal(suspect, R["L_DOCKED"])
            else:
                loyal(suspect, R["L_WRONG_ACCUSE"])
        # --- town ---
        hc = R["HEAL_COST"]*len(party)
        if any(p.hp < p.maxhp*0.6 for p in party) and gold - hc >= (reserve if policy == "hoarder" else 0):
            gold -= hc
            for p in party: p.hp = min(p.maxhp, p.hp + R["HEAL_AMT"])
        cap = 2 if policy == "hoarder" else 4
        if len(party) < cap:
            board = [recruit(avg_lvl) for _ in range(R["CANDIDATES"])]
            affordable = [c for c in board if gold - c.wage*R["SIGN_FEE_DAYS"] >= reserve + c.wage]
            if affordable:
                c = max(affordable, key=value); gold -= c.wage*R["SIGN_FEE_DAYS"]; party.append(hire(c))
        # --- combat (a saver skips the hunt to recover when the party is badly hurt) ---
        hurt = sum(p.hp for p in party) / sum(p.maxhp for p in party) < 0.5
        if not (policy == "accountant" and hurt):
            foes = [enemy(avg_lvl) for _ in range(spawn_count(len(party), day))]
            ledger = Counter(); fight(party, foes, ledger)
            out["clicks"] += ledger["clicks"]
            if not party: return day, "wipe", out
            gold += ledger["gold"] + R["COMMISSION"] + R["COMMISSION_PER_WEEK"]*((day-1)//7)
        else:
            out["rest_days"] += 1
        # --- night: tribute, payroll, desertion, mutiny, rest, theft ---
        t = tribute_due(day)
        if t:
            if gold >= t:
                gold -= t; out["tribute"] += t
                if day >= R["RUN_DAYS"]: return day, "victory", out
            else:
                strikes += 1
                if strikes >= R["STRIKES_TO_EXECUTION"] or day >= R["RUN_DAYS"]: return day, "executed", out
                gold = 0
                party.remove(max(party, key=lambda q: q.wage))   # collateral: best-paid hero
                if not party: return day, "abandoned", out
        paid = set()
        for p in party:
            if gold >= p.wage: gold -= p.wage; loyal(p, R["L_PAID"]); paid.add(p)
            else: loyal(p, R["L_UNPAID"])
        for p in [q for q in party if q.loyalty <= 0]:
            party.remove(p); gold -= int(gold*R["DESERT_CUT"])
        if not party: return day, "abandoned", out
        low = sum(1 for q in party if q.loyalty < R["MUTINY_LOW"])
        if len(party) >= 2 and (sum(q.loyalty for q in party)/len(party) < R["MUTINY_AVG"] or low >= 2):
            return day, "mutiny", out
        for p in paid: p.hp = min(p.maxhp, p.hp + round(p.maxhp*R["REST_HEAL_PAID"]))
        missing = 0
        for p in party:
            if gold > 0 and random.random() < p.greed*(1 - p.loyalty/R["THEFT_LOYALTY_DIV"])*R["THEFT_SCALE"]:
                cut = max(1, int(gold*random.uniform(*R["THEFT_CUT"]))); gold -= cut; p.stash += cut; missing += cut
        out["stolen"] += missing
        day += 1

ECONOMIC = ("mutiny", "executed", "abandoned")

def report(n=2000, seed=1, quiet=False):
    random.seed(seed); rows = {}
    for pol in ("naive", "accountant", "hoarder"):
        res = [run(pol) for _ in range(n)]
        days = sorted(d for d,_,_ in res); ends = Counter(e for _,e,_ in res)
        econ = sum(ends[e] for e in ECONOMIC)/n
        rows[pol] = dict(median=statistics.median(days), ends=ends, econ=econ, win=ends["victory"]/n, wipe=ends["wipe"]/n,
                         day1=sum(1 for d,e,_ in res if e == "wipe" and d == 1)/n)
        if not quiet:
            print(f"\n== policy: {pol}")
            print(f" days survived  median {statistics.median(days)}  p10 {days[n//10]}  p90 {days[9*n//10]}")
            print(f" endings        " + ", ".join(f"{k} {v/n:.0%}" for k,v in ends.most_common()))
            print(f" economic {econ:.0%} | wipe {rows[pol]['wipe']:.0%} | victory {rows[pol]['win']:.0%} | day-1 wipes {rows[pol]['day1']:.1%}")
            print(f" per run: stolen {statistics.mean(o['stolen'] for *_,o in res):.0f}g, recovered {statistics.mean(o['recovered'] for *_,o in res):.0f}g, tribute paid {statistics.mean(o['tribute'] for *_,o in res):.0f}g")
    return rows

def summary_line(rows):
    return " | ".join(f"{p}: win {v['win']:>4.0%} econ {v['econ']:>4.0%} wipe {v['wipe']:>4.0%} med {v['median']:>4}" for p, v in rows.items())

if __name__ == "__main__":
    if "--sweep" in sys.argv:
        grid = [dict(), dict(TRIBUTE=(150,300,450,650)), dict(TRIBUTE=(250,400,550,750)), dict(THEFT_SCALE=0.4),
                dict(SIGN_FEE_DAYS=1), dict(L_UNPAID=-20), dict(COMMISSION_PER_WEEK=15), dict(WAGE_RAISE=0.2)]
        for g in grid:
            R.clear(); R.update(RULES); R.update(g)
            print(f"{str(g) or 'baseline':34} {summary_line(report(n=800, quiet=True))}")
    else:
        report()
