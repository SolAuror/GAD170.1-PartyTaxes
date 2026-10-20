"""Monte Carlo model of Party Taxes combat + economy rules as implemented (v0.7 / Phase 2).
Mirrors PTManager/PTSoul/PTSoulGen constants, Prefab_Management.prefab overrides and
SoulType_*.asset ranges -- update this file when those change.

Usage:
  python3 Tools/BalanceSim/party_taxes_sim.py            # report for the current rules
  python3 Tools/BalanceSim/party_taxes_sim.py --sweep    # try alternative tuning values

Policies are simple scripted players: 'naive' spends freely, takes the best-paying job, never
audits or intervenes; 'accountant' keeps a full party, banks for the Queen in the second half of
each week, audits after thefts, picks jobs by risk, fights Aggressive, buys potions, retreats
from losing fights and takes the Goblin King when strong; 'hoarder' never hires beyond a pair,
banks everything, fights Cautious and takes the safest job.
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
    START_GOLD=45, PREFIX_CHANCE=0.15,
    HEAL_AMT=50, HEAL_COST=10,
    PARTY_DEF_BONUS=3,
    HP_PER_LEVEL=15, LEVEL_UP_HEAL=0.3,
    WAGE_RAISE=0.15, WAGE_COMPOUND=False,  # per level: compound x(1+raise), or linear +raise x base wage
    XP_POW=0.5,                          # XP to next level = 50*(level+1) * level^XP_POW
    REST_HEAL_PAID=0.25,
    ESCALATION_START=8, ESCALATION_EVERY=7,   # +1 max enemy per week from day 8 (jobs that escalate)
    # --- Phase 2: jobs, stances, interventions ---
    JOBS=(  # name, risk, min_day, weight, enemies (min,max) x party, escalates, level bonus, ambush chance, pay (base, per week), loyalty, rest heal
        ("Scouting patrol",      "Low",    1, 1.0, (0.5, 1.0),  False, 0, 1.0, (18, 9),  0, 0),
        ("Clear a goblin nest",  "Medium", 1, 1.0, (1.0, 1.25), True,  0, 1.0, (30, 15), 0, 0),
        ("Escort the tax wagon", "Low",    2, 0.8, (0.75, 1.0), False, 0, 0.5, (21, 9),  5, 0),
        ("Raid the goblin hoard","High",   5, 0.8, (1.5, 1.75), True,  0, 1.0, (51, 24), 0, 0),
        ("Hoblin war-camp",      "High",   8, 0.7, (1.0, 1.25), True,  3, 1.0, (66, 27), 0, 0),
        ("Bounty: a Goblin Lord","High",  12, 0.6, (0.75, 1.0), False, 4, 1.0, (78, 30), 3, 0),
    ),
    BOARD_SIZE=3, REST_HEAL=0.4,                 # plus an always-available rest day
    KING=dict(level_bonus=3, min_level=9, hp_mult=14.0, atk_mult=3.0, escorts=(0.75, 1.0), pay=400, score=500),
    STANCE=dict(aggressive=(1.15, 1.25), balanced=(1.0, 1.0), cautious=(0.85, 0.8)),   # (damage dealt, damage taken)
    POTION_COST=15, POTION_HEAL=40, HAZARD_COST=5, HAZARD_BONUS=0.25, HAZARD_LOYALTY=2,
    BRIBE_MULT=2, RETREAT_COST=0.1, RETREAT_COWARD_BELOW=50,
    ACTION_SECONDS=0.35, ROUND_PAUSE=0.6,        # 1x combat pacing

    # --- Phase 1: the Ledger ---
    RUN_DAYS=28, TRIBUTE=(250, 450, 650, 900), STRIKES_TO_EXECUTION=2,
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

def job_board(day):
    pool = [j for j in R["JOBS"] if j[2] <= day]
    board = []
    while pool and len(board) < R["BOARD_SIZE"]:
        j = random.choices(pool, weights=[x[3] for x in pool])[0]; board.append(j); pool.remove(j)
    return board

def job_pay(job, day): return job[8][0] + job[8][1]*((day-1)//7)

def job_foes(job, pc, avg_lvl, day):
    lo, hi = job[4]
    extra = (0 if day < R["ESCALATION_START"] else (day-R["ESCALATION_START"])//R["ESCALATION_EVERY"] + 1) if job[5] else 0
    n = random.randint(max(1, math.ceil(pc*lo)), max(1, math.ceil(pc*hi)) + extra)    # mirrors GetEncounterSize(job)
    return [enemy(avg_lvl + job[6]) for _ in range(n)]

def king_foes(pc, avg_lvl):
    k = R["KING"]
    boss = enemy(max(k["min_level"], avg_lvl + k["level_bonus"]))
    boss.maxhp = round(boss.maxhp*k["hp_mult"]); boss.hp = boss.maxhp; boss.atk = round(boss.atk*k["atk_mult"]); boss.gold *= 3
    lo, hi = k["escorts"]
    return [boss] + [enemy(avg_lvl) for _ in range(random.randint(math.ceil(pc*lo), math.ceil(pc*hi)))]

def pick_target(foes, stance, focus):
    if focus in foes: return focus
    if stance == "aggressive": return min(foes, key=lambda f: f.hp)          # finish the weakest
    if stance == "cautious": return max(foes, key=lambda f: f.atk)           # take out the biggest hitter
    return random.choice(foes)

def tactics(policy, party, foes, wallet, ledger, reserve, can_retreat=True):
    """One decision point per round. Returns 'retreat' or None; spends from wallet."""
    hp = sum(p.hp for p in party) / sum(p.maxhp for p in party)
    if policy == "accountant":
        weakest = min(party, key=lambda p: p.hp/p.maxhp)
        if weakest.hp < weakest.maxhp*0.3 and wallet["gold"] - R["POTION_COST"] >= reserve:
            wallet["gold"] -= R["POTION_COST"]; weakest.hp = min(weakest.maxhp, weakest.hp + R["POTION_HEAL"]); ledger["decisions"] += 1
        if can_retreat and hp < 0.25 and len(foes) >= len(party):
            return "retreat"
    elif policy == "hoarder" and can_retreat and hp < 0.35:
        return "retreat"
    return None

def fight(party, foes, ledger, stance="balanced", policy="naive", wallet=None, reserve=0, can_retreat=True):
    dealt, taken = R["STANCE"][stance]
    wallet = wallet if wallet is not None else {"gold": 0}
    while foes and party:
        ledger["rounds"] += 1
        if tactics(policy, party, foes, wallet, ledger, reserve, can_retreat) == "retreat":
            wallet["gold"] -= int(wallet["gold"]*R["RETREAT_COST"]); ledger["decisions"] += 1; ledger["retreated"] = 1
            return
        ps = sum(p.se for p in party)/len(party); es = sum(f.se for f in foes)/len(foes)
        for side in (("E","P") if es > ps else ("P","E")):
            if side == "P":
                for p in sorted(party, key=lambda x: -x.se):
                    if not foes: break
                    if not p.alive: continue
                    ledger["actions"] += 1; t = pick_target(foes, stance, None); t.take(round(p.deal()*dealt))
                    if not t.alive:
                        foes.remove(t); ledger["gold"] += t.gold; loyal(p, R["L_KILL"])
                        for q in party: q.gain(t.lifexp // len(party))
            else:
                for f in sorted(foes, key=lambda x: -x.se):
                    if not party: break
                    ledger["actions"] += 1; t = random.choice(party); t.take(round(f.deal()*taken))
                    if not t.alive:
                        party.remove(t)
                        for q in party: loyal(q, R["L_ALLY_DIED"])
                        for g in foes: g.gain(t.lifexp // len(foes))
            if not foes or not party: break

def value(s):                           # how good a hire looks to a scripted player: raw fighting strength
    return s.atk * s.maxhp * (1 + s.df/50)

RISK = {"Low": 0, "Medium": 1, "High": 2}

def choose_job(policy, board, party, day):
    hp = sum(p.hp for p in party) / sum(p.maxhp for p in party)
    if policy == "naive":
        return max(board, key=lambda j: job_pay(j, day))
    if policy == "hoarder":
        return min(board, key=lambda j: (RISK[j[1]], -job_pay(j, day))) if hp >= 0.5 else None
    allowed = 2 if len(party) >= 3 and hp >= 0.8 else (1 if hp >= 0.55 else (0 if hp >= 0.4 else -1))
    ok = [j for j in board if RISK[j[1]] <= allowed]
    return max(ok, key=lambda j: job_pay(j, day)) if ok else None      # None = rest day

STANCE_OF = dict(naive="balanced", accountant="aggressive", hoarder="cautious")

def run(policy):
    party = [hire(recruit()), hire(recruit())]; gold = R["START_GOLD"]; day = 1; strikes = 0
    out = Counter(); missing = 0; king = False
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
        # --- the day's job ---
        hp = sum(p.hp for p in party) / sum(p.maxhp for p in party)
        job = choose_job(policy, job_board(day), party, day)
        boss = (day == R["RUN_DAYS"] and policy == "accountant" and len(party) == 4 and hp >= 0.85
                and gold >= next_tribute(day))
        if boss or (job and random.random() < job[7]):
            foes = king_foes(len(party), avg_lvl) if boss else job_foes(job, len(party), avg_lvl, day)
            wallet = {"gold": gold}; ledger = Counter()
            fight(party, foes, ledger, STANCE_OF[policy], policy, wallet, reserve, can_retreat=not boss)   # no retreat from the King
            gold = wallet["gold"]
            out["fights"] += 1; out["rounds"] += ledger["rounds"]; out["actions"] += ledger["actions"]; out["decisions"] += ledger["decisions"] + 1
            out["seconds"] += ledger["actions"]*R["ACTION_SECONDS"] + ledger["rounds"]*R["ROUND_PAUSE"]
            out["max_seconds"] = max(out["max_seconds"], ledger["actions"]*R["ACTION_SECONDS"] + ledger["rounds"]*R["ROUND_PAUSE"])
            if boss: out["king_fights"] += 1
            if not party: return day, ("slain_by_king" if boss else "wipe"), out
            if ledger["retreated"]:
                out["retreats"] += 1
            else:
                gold += ledger["gold"] + (R["KING"]["pay"] if boss else job_pay(job, day))
                if boss: king = True
                elif job[9]:
                    for p in party: loyal(p, job[9])
        elif job:
            gold += job_pay(job, day)                       # the escort wasn't ambushed
            for p in party: loyal(p, job[9])
        else:
            out["rest_days"] += 1
            for p in party: p.hp = min(p.maxhp, p.hp + round(p.maxhp*R["REST_HEAL"]))
        # --- night: tribute, payroll, desertion, mutiny, rest, theft ---
        t = tribute_due(day)
        if t:
            if gold >= t:
                gold -= t; out["tribute"] += t
                if day >= R["RUN_DAYS"]: return day, ("vindicated" if king else "victory"), out
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
SLAIN = ("wipe", "slain_by_king")

def report(n=2000, seed=1, quiet=False):
    random.seed(seed); rows = {}
    for pol in ("naive", "accountant", "hoarder"):
        res = [run(pol) for _ in range(n)]
        days = sorted(d for d,_,_ in res); ends = Counter(e for _,e,_ in res)
        econ = sum(ends[e] for e in ECONOMIC)/n
        slain = sum(ends[e] for e in SLAIN)/n
        wins = ends["victory"] + ends["vindicated"]
        fights = sum(o["fights"] for *_,o in res) or 1
        rows[pol] = dict(median=statistics.median(days), ends=ends, econ=econ, win=wins/n, wipe=slain, king=ends["vindicated"]/n,
                         secs=sum(o["seconds"] for *_,o in res)/fights, decisions=sum(o["decisions"] for *_,o in res)/fights,
                         max_secs=max(o["max_seconds"] for *_,o in res),
                         day1=sum(1 for d,e,_ in res if e == "wipe" and d == 1)/n,
                         king_fights=sum(o["king_fights"] for *_,o in res)/n)
        if not quiet:
            print(f"\n== policy: {pol}")
            print(f" days survived  median {statistics.median(days)}  p10 {days[n//10]}  p90 {days[9*n//10]}")
            print(f" endings        " + ", ".join(f"{k} {v/n:.0%}" for k,v in ends.most_common()))
            print(f" economic {econ:.0%} | wipe {rows[pol]['wipe']:.0%} | won {rows[pol]['win']:.0%} (vindicated {rows[pol]['king']:.0%}) | day-1 wipes {rows[pol]['day1']:.1%}")
            print(f" per fight: {rows[pol]['secs']:.0f}s at 1x (longest {rows[pol]['max_secs']:.0f}s), {rows[pol]['decisions']:.1f} decisions; retreats/run {statistics.mean(o['retreats'] for *_,o in res):.1f}, rest days/run {statistics.mean(o['rest_days'] for *_,o in res):.1f}")
            print(f" per run: stolen {statistics.mean(o['stolen'] for *_,o in res):.0f}g, recovered {statistics.mean(o['recovered'] for *_,o in res):.0f}g, tribute paid {statistics.mean(o['tribute'] for *_,o in res):.0f}g")
    return rows

def summary_line(rows):
    return " | ".join(f"{p}: win {v['win']:>4.0%} (K {v['king']:>3.0%}) econ {v['econ']:>4.0%} wipe {v['wipe']:>4.0%} {v['secs']:>3.0f}s" for p, v in rows.items())

if __name__ == "__main__":
    if "--sweep" in sys.argv:
        grid = [dict(), dict(TRIBUTE=(200,400,600,800)), dict(TRIBUTE=(300,500,750,1000)), dict(THEFT_SCALE=0.4),
                dict(STANCE=dict(aggressive=(1.2, 1.15), balanced=(1.0, 1.0), cautious=(0.85, 0.8))),
                dict(KING=dict(RULES["KING"], hp_mult=10.0, atk_mult=2.5)), dict(POTION_COST=25), dict(RETREAT_COST=0.2)]
        for g in grid:
            R.clear(); R.update(RULES); R.update(g)
            print(f"{str(g) or 'baseline':34} {summary_line(report(n=800, quiet=True))}")
    else:
        report()
