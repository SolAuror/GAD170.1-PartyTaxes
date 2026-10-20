"""Monte Carlo model of Party Taxes combat + economy rules as implemented (v0.8 / Phase 3).
Mirrors PTManager/PTSoul/PTSoulGen constants, Prefab_Management.prefab overrides and
SoulType_*.asset ranges -- update this file when those change. Traits and night events are
read from the same JSON files the game loads (Assets/PartyTaxes/Resources/PartyTaxes).

Usage:
  python3 Tools/BalanceSim/party_taxes_sim.py            # report for the current rules (Clerk, Audit Level 1)
  python3 Tools/BalanceSim/party_taxes_sim.py --matrix   # accusations x audit levels for the accountant
  python3 Tools/BalanceSim/party_taxes_sim.py --variety  # how different two runs are by day 3
  python3 Tools/BalanceSim/party_taxes_sim.py --sweep    # try alternative tuning values

Policies are simple scripted players: 'naive' spends freely, takes the best-paying job, accepts
every raise, takes the first choice of every event and never audits or intervenes; 'accountant'
keeps a full party, banks for the Queen in the second half of each week, audits after thefts,
picks jobs by risk, picks contracts and event choices by expected value, counters raises, fights
Aggressive, buys potions, retreats from losing fights and takes the Goblin King when strong;
'hoarder' never hires beyond a pair, banks everything, refuses raises, fights Cautious.
"""
import json, math, os, random, statistics, sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "..", "Assets", "PartyTaxes", "Resources", "PartyTaxes")
TRAITS = {t["id"]: t for t in json.load(open(os.path.join(DATA, "Traits.json")))["traits"]}
EVENTS = json.load(open(os.path.join(DATA, "Events.json")))["events"]

ATTRS = ("m", "a", "c", "se", "l")          # Might, Agility, Constitution, Sense, Luck
TRAIT_ATTR = dict(might="m", agility="a", constitution="c", sense="se", luck="l")
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
    WAGE_RAISE=0.15,                     # each level adds this share of the base wage
    XP_POW=0.5,                          # XP to next level = 50*(level+1) * level^XP_POW
    REST_HEAL_PAID=0.25,
    ESCALATION_START=8, ESCALATION_EVERY=7,   # +1 max enemy per week from day 8 (jobs that escalate)
    # --- Phase 2: jobs, stances, interventions ---
    JOBS=(  # name, risk, min_day, weight, enemies (min,max) x party, escalates, level bonus, ambush chance, pay (base, per week), loyalty
        ("Scouting patrol",      "Low",    1, 1.0, (0.5, 1.0),  False, 0, 1.0, (18, 9),  0),
        ("Clear a goblin nest",  "Medium", 1, 1.0, (1.0, 1.25), True,  0, 1.0, (30, 15), 0),
        ("Escort the tax wagon", "Low",    2, 0.8, (0.75, 1.0), False, 0, 0.5, (21, 9),  5),
        ("Raid the goblin hoard","High",   5, 0.8, (1.5, 1.75), True,  0, 1.0, (51, 24), 0),
        ("Hoblin war-camp",      "High",   8, 0.7, (1.0, 1.25), True,  3, 1.0, (66, 27), 0),
        ("Bounty: a Goblin Lord","High",  12, 0.6, (0.75, 1.0), False, 4, 1.0, (78, 30), 3),
    ),
    BOARD_SIZE=3, REST_HEAL=0.4,                 # plus an always-available rest day
    KING=dict(level_bonus=3, min_level=9, hp_mult=14.0, atk_mult=3.0, escorts=(0.75, 1.0), pay=400),
    STANCE=dict(aggressive=(1.15, 1.25), balanced=(1.0, 1.0), cautious=(0.85, 0.8)),   # (damage dealt, damage taken)
    POTION_COST=15, POTION_HEAL=40, RETREAT_COST=0.1,
    ACTION_SECONDS=0.35, ROUND_PAUSE=0.6,        # 1x combat pacing
    # --- Phase 1: the Ledger ---
    RUN_DAYS=28, TRIBUTE=(250, 450, 650, 900), STRIKES_TO_EXECUTION=2,
    CANDIDATES=3, SIGN_FEE_DAYS=2,      # signing fee = candidate wage x this
    LOYALTY_START=60,
    L_PAID=5, L_UNPAID=-15, L_ALLY_DIED=-10, L_KILL=3, L_WRONG_ACCUSE=-15, L_DOCKED=-10,
    GREED=(0.1, 0.9), THEFT_SCALE=0.7, THEFT_CUT=(0.05, 0.12), THEFT_LOYALTY_DIV=150,  # loyal staff still skim, just less
    DESERT_CUT=0.1, MUTINY_AVG=20, MUTINY_LOW=10,
    AUDIT_BASE=0.8, AUDIT_PER_SENSE=0.05, AUDIT_RANGE=(0.4, 0.9),
    # --- Phase 3: traits, contracts, negotiation, events, accusations, audit levels ---
    SECOND_TRAIT_CHANCE=0.35,
    SHARE_PERCENT=0.16, SHARE_FEE_DAYS=1, SHARE_NO_TAKE_LOYALTY=-5,   # loot share: no wage, 16% of each job's takings
    BONUS_FEE_DAYS=5, BONUS_WAGE=0.6,                                  # signing bonus: big fee, 60% wage for good
    RAISE_ACCEPT_LOYALTY=5, RAISE_REFUSE_LOYALTY=-15, RAISE_COUNTER_FAIL_LOYALTY=-10,
    COUNTER_BASE=0.6, COUNTER_PER_SENSE=0.05, COUNTER_RANGE=(0.3, 0.8),
    EVENT_CHANCE=0.7,
    HUNGOVER_CHANCE=0.3, HUNGOVER_DAMAGE=0.7, BLOODTHIRSTY_NO_KILL=-10, UNION_REP_EXTRA=-5,
    GLORY_LOYALTY=5,
    ACCUSATION="clerk", AUDIT_LEVEL=1,
)
ACCUSATIONS = ("clerk", "embezzler", "scapegoat", "bookkeeper")
LEVEL_RULES = {  # cumulative modifiers per Audit Level (mirrors PTRunSetup)
    2: dict(tribute=1.08), 3: dict(theft=1.3), 4: dict(fees=1.5), 5: dict(extra_enemies=1, tribute=1.08),
}

r = lambda lo_hi: random.randint(lo_hi[0], lo_hi[1])
R = dict(RULES)

def level_mods():
    m = dict(tribute=1.0, theft=1.0, candidates=R["CANDIDATES"], fees=1.0, strikes=R["STRIKES_TO_EXECUTION"], extra_enemies=0)
    for lvl in range(2, R["AUDIT_LEVEL"] + 1):
        for k, v in LEVEL_RULES[lvl].items():
            m[k] = m[k] * v if k in ("tribute", "theft", "fees") else (m[k] + v if k == "extra_enemies" else v)
    if R["ACCUSATION"] == "embezzler": m["tribute"] *= 1.2
    return m

class Soul:
    def __init__(s, attrs, adversary, affinity):
        s.m, s.a, s.c, s.se, s.l = attrs
        s.adv = adversary; s.aff = affinity; s.level = 1; s.xp = 0; s.wage = s.base_wage = 0
        s.gold = 0; s.lifexp = 25; s.bonus_hp = 0; s.traits = []; s.requested = 0
        s.contract = "wage"; s.hungover = False; s.took_today = False
        if random.random() < R["PREFIX_CHANCE"]:
            k = random.choice(ATTRS)
            setattr(s, k, max(1, getattr(s, k) + random.choice([-2,-1,1,2,3])))
        s.hp = 0; s.recalc(); s.hp = s.maxhp
    def t(s, field, default=0):            # sum of a trait field over this soul's traits
        return sum(TRAITS[x].get(field, default) for x in s.traits)
    def has(s, special): return any(TRAITS[x].get("special") == special for x in s.traits)
    def recalc(s):
        s.maxhp = 60 + s.c*10 + s.bonus_hp; s.atk = s.m*4
        s.df = s.c*2 + (0 if s.adv else R["PARTY_DEF_BONUS"])
        s.crit = min(s.l*3, 75); s.cm = 1.5 + 0.02*(s.l+s.m+s.se)
        s.hp = min(s.hp, s.maxhp)
    @property
    def alive(s): return s.hp > 0
    def next_xp(s): return round((s.level+1)*50 * s.level**R["XP_POW"])
    def deal(s):
        d = round(s.atk*s.cm) if random.randrange(100) < s.crit else s.atk
        if not s.adv:
            d = d * (1 + s.t("damagePercent")/100) * (R["HUNGOVER_DAMAGE"] if s.hungover else 1)
        return d
    def take(s, dmg):
        d = max(1, round(dmg*(1 - s.df/(s.df+50)))); s.hp = max(0, s.hp-d)
    def wage_for_level(s):
        return round(s.base_wage*(1 + R["WAGE_RAISE"]*(s.level-1)))
    def level_up(s, negotiate=True):
        s.level += 1; s.lifexp += s.xp
        setattr(s, s.aff, getattr(s, s.aff) + 1); s.bonus_hp += R["HP_PER_LEVEL"]
        s.recalc(); s.hp = min(s.maxhp, s.hp + round(s.maxhp*R["LEVEL_UP_HEAL"]))
        if s.adv or s.contract == "share": return
        if negotiate and hasattr(s, "loyalty"): s.requested = s.wage_for_level()   # asks for a raise
        else: s.wage = s.wage_for_level()
    def gain(s, x):
        s.xp += x
        while s.xp >= s.next_xp():
            s.xp -= s.next_xp(); s.level_up()

def roll_traits():
    first = random.choices(list(TRAITS), weights=[t["weight"] for t in TRAITS.values()])[0]
    traits = [first]
    if random.random() < R["SECOND_TRAIT_CHANCE"]:
        pool = [k for k in TRAITS if k != first and k not in TRAITS[first].get("excludes", []) and first not in TRAITS[k].get("excludes", [])]
        traits.append(random.choices(pool, weights=[TRAITS[k]["weight"] for k in pool])[0])
    return traits

def recruit(avg_level=0, traits=None):
    f = random.choice(list(FRIENDLY.values()))
    s = Soul(tuple(r(x) for x in f[:5]), False, f[6])
    s.traits = traits if traits is not None else roll_traits()
    for field, attr in TRAIT_ATTR.items():
        setattr(s, attr, max(1, getattr(s, attr) + s.t(field)))
    s.base_wage = max(1, round(r(f[5]) * (1 + s.t("wagePercent")/100)))
    s.wage = s.base_wage; s.recalc(); s.hp = s.maxhp
    while s.level < avg_level + s.t("levelBonus"): s.level_up(negotiate=False)
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

def hire(soul, contract="wage", loyalty=None):
    soul.loyalty = min(100, (R["LOYALTY_START"] if loyalty is None else loyalty) + soul.t("startLoyalty"))
    soul.greed = min(1, max(0, random.uniform(*R["GREED"]) + soul.t("greed")))
    soul.stash = 0; soul.contract = contract
    if contract == "share": soul.wage = 0
    if contract == "bonus":
        soul.base_wage = max(1, round(soul.base_wage*R["BONUS_WAGE"])); soul.wage = soul.wage_for_level()
    return soul

def fee(c, contract, mods, cowards=False):
    days = {"wage": R["SIGN_FEE_DAYS"], "share": R["SHARE_FEE_DAYS"], "bonus": R["BONUS_FEE_DAYS"]}[contract]
    return round(c.wage*days*mods["fees"]*(1.5 if cowards else 1))

def greed_hint(s):                      # what the player can see: "honest face" / "hard to read" / "eyes your purse"
    return 0.2 if s.greed < 0.35 else (0.8 if s.greed > 0.65 else 0.5)

def loyal(s, d): s.loyalty = max(0, min(100, s.loyalty + d))

def tribute_amount(week_index, party, mods, adjust):
    base = R["TRIBUTE"][week_index] * mods["tribute"]
    discount = max([p.t("tributeDiscountPercent") for p in party] or [0])
    return max(0, round(base * (1 - discount/100) / 5) * 5 + adjust)

# ── jobs and combat ───────────────────────────────────────────────
def job_board(day):
    pool = [j for j in R["JOBS"] if j[2] <= day]
    board = []
    while pool and len(board) < R["BOARD_SIZE"]:
        j = random.choices(pool, weights=[x[3] for x in pool])[0]; board.append(j); pool.remove(j)
    return board

def job_pay(job, day): return job[8][0] + job[8][1]*((day-1)//7)

def job_foes(job, pc, avg_lvl, day, extra_enemies=0):
    lo, hi = job[4]
    extra = (0 if day < R["ESCALATION_START"] else (day-R["ESCALATION_START"])//R["ESCALATION_EVERY"] + 1) if job[5] else 0
    n = random.randint(max(1, math.ceil(pc*lo)), max(1, math.ceil(pc*hi)) + extra) + extra_enemies    # mirrors GetEncounterSize(job)
    return [enemy(avg_lvl + job[6]) for _ in range(n)]

def king_foes(pc, avg_lvl):
    k = R["KING"]
    boss = enemy(max(k["min_level"], avg_lvl + k["level_bonus"]))
    boss.maxhp = round(boss.maxhp*k["hp_mult"]); boss.hp = boss.maxhp; boss.atk = round(boss.atk*k["atk_mult"]); boss.gold *= 3
    lo, hi = k["escorts"]
    return [boss] + [enemy(avg_lvl) for _ in range(random.randint(math.ceil(pc*lo), math.ceil(pc*hi)))]

def pick_target(foes, stance):
    if stance == "aggressive": return min(foes, key=lambda f: f.hp)          # finish the weakest
    if stance == "cautious": return max(foes, key=lambda f: f.atk)           # take out the biggest hitter
    return random.choice(foes)

def tactics(policy, party, foes, wallet, ledger, reserve, can_retreat=True):
    """One decision point per round. Returns 'retreat' or None; spends from wallet."""
    hp = sum(p.hp for p in party) / sum(p.maxhp for p in party)
    if policy == "accountant":
        weakest = min(party, key=lambda p: p.hp/p.maxhp)
        if weakest.hp < weakest.maxhp*0.3 and wallet["gold"] - R["POTION_COST"] >= reserve:
            heal = R["POTION_HEAL"] * (1 + max([p.t("potionPercent") for p in party] or [0])/100)
            wallet["gold"] -= R["POTION_COST"]; weakest.hp = min(weakest.maxhp, round(weakest.hp + heal)); ledger["decisions"] += 1
        if can_retreat and hp < 0.25 and len(foes) >= len(party):
            return "retreat"
    elif policy == "hoarder" and can_retreat and hp < 0.35:
        return "retreat"
    return None

def fight(party, foes, ledger, stance="balanced", policy="naive", wallet=None, reserve=0, can_retreat=True):
    dealt, taken = R["STANCE"][stance]
    wallet = wallet if wallet is not None else {"gold": 0}
    kills = Counter()
    while foes and party:
        ledger["rounds"] += 1
        if tactics(policy, party, foes, wallet, ledger, reserve, can_retreat) == "retreat":
            wallet["gold"] -= int(wallet["gold"]*R["RETREAT_COST"]); ledger["decisions"] += 1; ledger["retreated"] = 1
            return kills
        ps = sum(p.se for p in party)/len(party); es = sum(f.se for f in foes)/len(foes)
        for side in (("E","P") if es > ps else ("P","E")):
            if side == "P":
                for p in sorted(party, key=lambda x: -x.se):
                    if not foes: break
                    if not p.alive: continue
                    ledger["actions"] += 1; t = pick_target(foes, stance); t.take(round(p.deal()*dealt))
                    if not t.alive:
                        foes.remove(t); kills[p] += 1
                        ledger["gold"] += round(t.gold * (1 + p.t("lootPercent")/100)); loyal(p, R["L_KILL"])
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
    return kills

def value(s):                           # how good a hire looks to a scripted player: raw fighting strength
    return s.atk * s.maxhp * (1 + s.df/50) * (1 + s.t("damagePercent")/100)

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

# ── night events ──────────────────────────────────────────────────
def eligible_events(day, party, gold, seen):
    out = []
    for ev in EVENTS:
        if ev["id"] in seen or day < ev.get("minDay", 1) or (ev.get("maxDay", 0) and day > ev["maxDay"]): continue
        if len(party) < ev.get("minParty", 1): continue
        need = ev.get("requiresTrait", "")
        if need and not any(need in p.traits for p in party): continue
        out.append(ev)
    return out

def choice_ok(ch, gold):                 # a choice that costs gold needs the gold
    return gold + ch.get("success", {}).get("gold", 0) >= 0

def effect_value(eff, party, gold):      # a rough score a careful player might use
    v = eff.get("gold", 0) + gold*eff.get("goldPercent", 0)/100 - eff.get("tributeChange", 0)
    v += 1.5*eff.get("partyLoyalty", 0)*len(party) + 1.5*eff.get("subjectLoyalty", 0)
    v += 0.8*eff.get("partyHealPercent", 0)*len(party) + 0.8*eff.get("subjectHealPercent", 0)
    v += 20*eff.get("subjectMight", 0) + 20*eff.get("subjectConstitution", 0) + 0.3*eff.get("subjectXp", 0)
    v -= 40*eff.get("subjectGreed", 0)*4 + 25*max(0, eff.get("subjectCowardly", 0)) + 60*eff.get("subjectLeaves", False)
    v -= 0.5*eff.get("subjectWagePercent", 0) - 0.6*eff.get("nextJobPayPercent", 0)
    return v

def choice_value(ch, party, gold):
    p = ch.get("chance", 1) or 1
    return p*effect_value(ch.get("success", {}), party, gold) + (1-p)*effect_value(ch.get("failure", {}), party, gold)

def apply_effect(eff, subject, party, state):
    state["gold"] = max(0, state["gold"] + eff.get("gold", 0) + int(state["gold"]*eff.get("goldPercent", 0)/100))
    for p in party: loyal(p, eff.get("partyLoyalty", 0))
    for p in party:
        if eff.get("partyHealPercent", 0): p.hp = max(1, min(p.maxhp, p.hp + round(p.maxhp*eff["partyHealPercent"]/100)))
    state["tribute_adjust"] += eff.get("tributeChange", 0)
    state["next_job_pay"] += eff.get("nextJobPayPercent", 0)
    if subject not in party: return
    loyal(subject, eff.get("subjectLoyalty", 0))
    if eff.get("subjectHealPercent", 0): subject.hp = max(1, min(subject.maxhp, subject.hp + round(subject.maxhp*eff["subjectHealPercent"]/100)))
    subject.greed = min(1, max(0, subject.greed + eff.get("subjectGreed", 0)))
    subject.m += eff.get("subjectMight", 0); subject.c += eff.get("subjectConstitution", 0); subject.recalc()
    if eff.get("subjectXp", 0): subject.gain(eff["subjectXp"])
    if eff.get("subjectWagePercent", 0):
        subject.base_wage = round(subject.base_wage*(1 + eff["subjectWagePercent"]/100))
        if subject.contract != "share": subject.wage = subject.wage_for_level()
    if eff.get("subjectLeaves"): party.remove(subject)

def night_event(policy, day, party, state, seen, out):
    if random.random() >= R["EVENT_CHANCE"]: return
    pool = eligible_events(day, party, state["gold"], seen)
    if not pool: return
    ev = random.choices(pool, weights=[e.get("weight", 1) for e in pool])[0]
    seen.add(ev["id"]); out["events"] += 1
    need = ev.get("requiresTrait", "")
    subject = random.choice([p for p in party if not need or need in p.traits])
    choices = [c for c in ev["choices"] if choice_ok(c, state["gold"])] or ev["choices"][-1:]
    if policy == "accountant": ch = max(choices, key=lambda c: choice_value(c, party, state["gold"]))
    elif policy == "hoarder": ch = max(choices, key=lambda c: c.get("success", {}).get("gold", 0) - c.get("success", {}).get("tributeChange", 0))
    else: ch = choices[0]
    p = ch.get("chance", 1) or 1
    apply_effect(ch.get("success", {}) if random.random() < p else ch.get("failure", {}), subject, party, state)

# ── a run ─────────────────────────────────────────────────────────
def choose_contract(policy, c, gold, reserve, mods, day):
    if policy != "accountant": return "wage"
    if fee(c, "bonus", mods) + reserve + c.wage <= gold and day <= 14: return "bonus"   # pays for itself in ~8 days
    if fee(c, "wage", mods) + reserve + c.wage > gold: return "share"                   # can't afford a wage: offer a cut
    return "wage"

def negotiate(policy, p):
    if not p.requested or p.requested <= p.wage: p.requested = 0; return
    if policy == "hoarder":
        loyal(p, R["RAISE_REFUSE_LOYALTY"])
    elif policy == "accountant" and p.loyalty >= 50:
        chance = min(R["COUNTER_RANGE"][1], max(R["COUNTER_RANGE"][0], R["COUNTER_BASE"] - (p.se - 3)*R["COUNTER_PER_SENSE"]))
        if random.random() < chance: p.wage = (p.wage + p.requested)//2
        else: p.wage = p.requested; loyal(p, R["RAISE_COUNTER_FAIL_LOYALTY"])
    else:
        p.wage = p.requested; loyal(p, R["RAISE_ACCEPT_LOYALTY"])
    p.requested = 0

def run(policy, track_variety=False):
    mods = level_mods(); acc = R["ACCUSATION"]
    start_loyalty = 50 if acc == "bookkeeper" else R["LOYALTY_START"]
    party = [hire(recruit(), loyalty=start_loyalty), hire(recruit(), loyalty=start_loyalty)]
    if acc == "scapegoat":                 # one starting member is a Loyal Veteran who owes you: no wage premium
        vet = recruit(traits=["loyal", "veteran"])
        vet.base_wage = round(vet.base_wage / (1 + vet.t("wagePercent")/100)); vet.wage = vet.wage_for_level()
        party[0] = hire(vet, loyalty=start_loyalty)
    start_gold = R["START_GOLD"] + {"embezzler": 100, "bookkeeper": -15}.get(acc, 0)
    state = dict(gold=start_gold, tribute_adjust=0, next_job_pay=0)
    day = 1; strikes = 0; out = Counter(); missing = 0; king = False; seen = set(); thief = None
    variety = set()
    if track_variety:
        for p in party: variety.update("trait:" + t for t in p.traits)
    while True:
        gold = state["gold"]
        avg_lvl = sum(p.level for p in party)//len(party)
        wages = sum(p.wage for p in party)
        week = (day - 1)//7
        due_next = tribute_amount(min(week, 3), party, mods, state["tribute_adjust"])
        reserve = 0
        into_week = (day - 1) % 7 + 1
        if policy == "accountant": reserve = due_next * max(0, into_week - 3) / 4 + wages
        elif policy == "hoarder": reserve = due_next + wages
        # --- morning: raises, audit ---
        for p in party: negotiate(policy, p)
        if policy == "accountant" and missing > 0 and party:
            suspect = thief if (acc == "bookkeeper" and thief in party) else max(party, key=lambda q: greed_hint(q) * (100 - q.loyalty))
            if suspect.stash > 0:
                bonus = max([p.t("auditBonusPercent") for p in party] or [0]) / 100
                chance = min(R["AUDIT_RANGE"][1], max(R["AUDIT_RANGE"][0], R["AUDIT_BASE"] - (suspect.se - 3)*R["AUDIT_PER_SENSE"])) + bonus
                if random.random() < chance:
                    gold += suspect.stash; out["recovered"] += suspect.stash; suspect.stash = 0; loyal(suspect, R["L_DOCKED"])
            else:
                loyal(suspect, R["L_WRONG_ACCUSE"])
        # --- town ---
        discount = max([p.t("clericDiscountPercent") for p in party] or [0])
        hc = round(R["HEAL_COST"]*len(party)*(1 - discount/100))
        if any(p.hp < p.maxhp*0.6 for p in party) and gold - hc >= (reserve if policy == "hoarder" else 0):
            gold -= hc
            for p in party: p.hp = min(p.maxhp, p.hp + R["HEAL_AMT"])
        board = [recruit(avg_lvl) for _ in range(mods["candidates"])]
        if track_variety and day <= 3:
            for c in board: variety.update("trait:" + t for t in c.traits)
        cap = 2 if policy == "hoarder" else 4
        if len(party) < cap:
            cowards = any("coward" in p.traits for p in party)
            options = [(c, choose_contract(policy, c, gold, reserve, mods, day)) for c in board]
            affordable = [(c, k) for c, k in options if gold - fee(c, k, mods, cowards) >= reserve + c.wage*(k != "share")]
            if affordable:
                c, k = max(affordable, key=lambda ck: value(ck[0])); gold -= fee(c, k, mods, cowards)
                party.append(hire(c, k, start_loyalty)); out["contract_" + k] += 1
        # --- the day's job ---
        for p in party: p.took_today = False
        hp = sum(p.hp for p in party) / sum(p.maxhp for p in party)
        offered = job_board(day)
        if track_variety and day <= 3: variety.update("job:" + j[0] for j in offered)
        job = choose_job(policy, offered, party, day)
        boss = (day == R["RUN_DAYS"] and policy == "accountant" and len(party) == 4 and hp >= 0.85 and gold >= due_next)
        earned = 0
        if boss or (job and random.random() < job[7]):
            foes = king_foes(len(party), avg_lvl) if boss else job_foes(job, len(party), avg_lvl, day, mods["extra_enemies"])
            wallet = {"gold": gold}; ledger = Counter()
            kills = fight(party, foes, ledger, STANCE_OF[policy], policy, wallet, reserve, can_retreat=not boss)
            gold = wallet["gold"]
            out["fights"] += 1; out["rounds"] += ledger["rounds"]; out["actions"] += ledger["actions"]; out["decisions"] += ledger["decisions"] + 1
            out["seconds"] += ledger["actions"]*R["ACTION_SECONDS"] + ledger["rounds"]*R["ROUND_PAUSE"]
            if boss: out["king_fights"] += 1
            if not party: return day, ("slain_by_king" if boss else "wipe"), out, variety
            if ledger["retreated"]:
                out["retreats"] += 1
            else:
                pay = R["KING"]["pay"] if boss else round(job_pay(job, day) * (1 + state["next_job_pay"]/100))
                earned = ledger["gold"] + pay
                if boss: king = True
                elif job[9]:
                    for p in party: loyal(p, job[9])
                for p in party:
                    if p.has("bloodthirsty") and kills[p] == 0: loyal(p, R["BLOODTHIRSTY_NO_KILL"])
                    if p.has("glory_hound") and job and job[1] == "High": loyal(p, R["GLORY_LOYALTY"])
            state["next_job_pay"] = 0
        elif job:
            earned = round(job_pay(job, day) * (1 + state["next_job_pay"]/100)); state["next_job_pay"] = 0
            for p in party: loyal(p, job[9])
        else:
            out["rest_days"] += 1
            for p in party:
                p.hp = min(p.maxhp, p.hp + round(p.maxhp*R["REST_HEAL"]))
                if p.has("glory_hound"): loyal(p, -R["GLORY_LOYALTY"])
        for p in party: p.hungover = False
        if earned:
            for p in party:
                if p.contract == "share":
                    cut = round(earned*R["SHARE_PERCENT"]); earned -= cut; out["share_paid"] += cut; p.took_today = True
            gold += earned
        # --- night: tribute, payroll, desertion, mutiny, rest, theft, event ---
        if day % 7 == 0:
            t = tribute_amount(day//7 - 1, party, mods, state["tribute_adjust"]); state["tribute_adjust"] = 0
            if gold >= t:
                gold -= t; out["tribute"] += t
                if day >= R["RUN_DAYS"]: return day, ("vindicated" if king else "victory"), out, variety
            else:
                strikes += 1
                if strikes >= mods["strikes"] or day >= R["RUN_DAYS"]: return day, "executed", out, variety
                gold = 0
                seizable = [q for q in party if not any(TRAITS[x].get("immuneToSeizure") for x in q.traits)]
                if seizable: party.remove(max(seizable, key=lambda q: q.wage))   # collateral: best-paid hero
                if not party: return day, "abandoned", out, variety
        paid = set()
        for p in party:
            if p.has("gambler") and p.contract != "share":
                p.wage = max(1, round(p.wage_for_level() * random.uniform(0.5, 1.5)))
            if p.contract == "share":
                loyal(p, R["L_PAID"] + p.t("paidLoyalty") if p.took_today else R["SHARE_NO_TAKE_LOYALTY"]); paid.add(p)
            elif gold >= p.wage:
                gold -= p.wage; loyal(p, R["L_PAID"] + p.t("paidLoyalty")); paid.add(p)
                if p.has("drunkard") and random.random() < R["HUNGOVER_CHANCE"]: p.hungover = True
            else:
                loyal(p, round(R["L_UNPAID"] * (1 + p.t("unpaidLoyaltyPercent")/100)))
                if p.has("union_rep"):
                    for q in party:
                        if q is not p: loyal(q, R["UNION_REP_EXTRA"])
        for p in [q for q in party if q.loyalty <= 0]:
            party.remove(p); gold -= int(gold*R["DESERT_CUT"])
        if not party: return day, "abandoned", out, variety
        low = sum(1 for q in party if q.loyalty < R["MUTINY_LOW"])
        if len(party) >= 2 and (sum(q.loyalty for q in party)/len(party) < R["MUTINY_AVG"] or low >= 2):
            return day, "mutiny", out, variety
        rest = R["REST_HEAL_PAID"] + max([p.t("restPercent") for p in party] or [0])/100
        for p in paid: p.hp = min(p.maxhp, p.hp + round(p.maxhp*rest))
        missing = 0; thief = None
        for p in party:
            if gold > 0 and random.random() < p.greed*(1 - p.loyalty/R["THEFT_LOYALTY_DIV"])*R["THEFT_SCALE"]*mods["theft"]:
                cut = max(1, int(gold*random.uniform(*R["THEFT_CUT"]))); gold -= cut; p.stash += cut; missing += cut
                if thief is None or cut > 0: thief = p
        out["stolen"] += missing
        state["gold"] = gold
        night_event(policy, day, party, state, seen, out)
        if track_variety and day <= 3: variety.update("event:" + e for e in seen)
        if not party: return day, "abandoned", out, variety
        day += 1

ECONOMIC = ("mutiny", "executed", "abandoned")
SLAIN = ("wipe", "slain_by_king")

def report(n=2000, seed=1, quiet=False, policies=("naive", "accountant", "hoarder")):
    random.seed(seed); rows = {}
    for pol in policies:
        res = [run(pol) for _ in range(n)]
        days = sorted(d for d,_,_,_ in res); ends = Counter(e for _,e,_,_ in res)
        econ = sum(ends[e] for e in ECONOMIC)/n
        slain = sum(ends[e] for e in SLAIN)/n
        wins = ends["victory"] + ends["vindicated"]
        fights = sum(o["fights"] for *_,o,_ in res) or 1
        rows[pol] = dict(median=statistics.median(days), ends=ends, econ=econ, win=wins/n, wipe=slain, king=ends["vindicated"]/n,
                         secs=sum(o["seconds"] for *_,o,_ in res)/fights, decisions=sum(o["decisions"] for *_,o,_ in res)/fights)
        if not quiet:
            mean = lambda k: statistics.mean(o[k] for *_,o,_ in res)
            print(f"\n== policy: {pol}")
            print(f" days survived  median {statistics.median(days)}  p10 {days[n//10]}  p90 {days[9*n//10]}")
            print(f" endings        " + ", ".join(f"{k} {v/n:.0%}" for k,v in ends.most_common()))
            print(f" economic {econ:.0%} | slain {slain:.0%} | won {wins/n:.0%} (vindicated {ends['vindicated']/n:.0%})")
            print(f" per fight: {rows[pol]['secs']:.0f}s at 1x, {rows[pol]['decisions']:.1f} decisions | events/run {mean('events'):.1f}")
            print(f" per run: stolen {mean('stolen'):.0f}g, recovered {mean('recovered'):.0f}g, tribute paid {mean('tribute'):.0f}g, share cuts {mean('share_paid'):.0f}g")
            hires = sum(sum(o[k] for *_,o,_ in res) for k in ("contract_wage","contract_share","contract_bonus")) or 1
            print(f" contracts: wage {sum(o['contract_wage'] for *_,o,_ in res)/hires:.0%}, share {sum(o['contract_share'] for *_,o,_ in res)/hires:.0%}, bonus {sum(o['contract_bonus'] for *_,o,_ in res)/hires:.0%}")
    return rows

def summary_line(rows):
    return " | ".join(f"{p}: win {v['win']:>4.0%} (K {v['king']:>3.0%}) econ {v['econ']:>4.0%} slain {v['wipe']:>4.0%}" for p, v in rows.items())

def matrix(n=800):
    print(f"{'':12}" + "".join(f"  L{l}: win/econ/slain " for l in range(1, 6)))
    for acc in ACCUSATIONS:
        cells = []
        for lvl in range(1, 6):
            R.clear(); R.update(RULES); R.update(ACCUSATION=acc, AUDIT_LEVEL=lvl)
            a = report(n=n, quiet=True, policies=("accountant",))["accountant"]
            cells.append(f"  {a['win']:>4.0%} {a['econ']:>4.0%} {a['wipe']:>4.0%}      ")
        print(f"{acc:12}" + "".join(cells))
    R.clear(); R.update(RULES)

def variety(n=300, seed=7):
    """Average Jaccard overlap of what two runs show by day 3 (traits seen, events, jobs offered)."""
    random.seed(seed)
    sets = [run("accountant", track_variety=True)[3] for _ in range(n)]
    sims = []
    for i in range(n):
        for j in range(i+1, min(n, i+20)):
            a, b = sets[i], sets[j]
            sims.append(len(a & b) / max(1, len(a | b)))
    traits = statistics.mean(len([x for x in s if x.startswith("trait:")]) for s in sets)
    events = statistics.mean(len([x for x in s if x.startswith("event:")]) for s in sets)
    print(f"by day 3: {traits:.1f} distinct traits seen, {events:.1f} night events; two runs share {statistics.mean(sims):.0%} of this content on average")

if __name__ == "__main__":
    if "--matrix" in sys.argv:
        matrix()
    elif "--variety" in sys.argv:
        variety()
    elif "--sweep" in sys.argv:
        grid = [dict(), dict(SHARE_PERCENT=0.12), dict(SHARE_PERCENT=0.2), dict(BONUS_WAGE=0.5), dict(EVENT_CHANCE=0.5),
                dict(TRIBUTE=(225,425,600,850)), dict(TRIBUTE=(275,475,700,950))]
        for g in grid:
            R.clear(); R.update(RULES); R.update(g)
            print(f"{str(g) or 'baseline':34} {summary_line(report(n=800, quiet=True))}")
    else:
        report()
