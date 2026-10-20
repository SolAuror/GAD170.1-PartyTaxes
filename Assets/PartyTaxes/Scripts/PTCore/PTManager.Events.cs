using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PartyTaxes;


/// PTManager partial — Events Part: night events (drawn at night, decided in the morning) and raise
/// negotiations (asked for after a level-up). Pending decisions block jobs and sleep until answered.
/// Event content lives in Resources/PartyTaxes/Events.json (see PTContent).
public partial class PTManager
{
    public enum RaiseChoice { Accept, Refuse, Counter }

    private const float NIGHT_EVENT_CHANCE = 0.7f;                                                           //most nights, something happens
    private const int RAISE_ACCEPT_LOYALTY = 5;
    private const int RAISE_REFUSE_LOYALTY = -15;
    private const int RAISE_COUNTER_FAIL_LOYALTY = -10;
    private const float COUNTER_BASE_CHANCE = 0.6f;                                                          //chance to talk them down to half the raise (Sense 3)
    private const float COUNTER_CHANCE_PER_SENSE = 0.05f;                                                    //sharper characters drive a harder bargain
    private const float COUNTER_MIN_CHANCE = 0.3f;
    private const float COUNTER_MAX_CHANCE = 0.8f;

    public PTNightEvent PendingEvent { get; private set; }
    public PTSoul EventSubject { get; private set; }
    public int TributeAdjustment { get; private set; }                                                       //events can raise or lower the next tribute
    public int NextJobPayPercent { get; private set; }                                                       //events can change the next job's pay
    private readonly HashSet<string> seenEvents = new HashSet<string>();                                     //each event at most once per run

    public bool HasPendingDecision => PendingConfrontation != null || PendingRaise != null || PendingEvent != null;

    string PendingDecisionName => PendingConfrontation != null ? "what to do about " + PendingConfrontation.Name
                                : PendingRaise != null ? PendingRaise.Name + "'s raise"
                                : PendingEvent != null ? PendingEvent.title : "";

#region Night events
    void DrawNightEvent()                                                                                    //called at night; the player decides in the morning
    {
        PendingEvent = null;
        EventSubject = null;
        if (partyMembers.Count == 0 || Random.value >= NIGHT_EVENT_CHANCE) return;

        List<PTNightEvent> eligible = PTContent.Events.Where(e => EventEligible(e)).ToList();
        if (eligible.Count == 0) return;
        float total = eligible.Sum(e => e.Weight);
        float roll = Random.value * total;
        PTNightEvent picked = eligible[eligible.Count - 1];
        foreach (PTNightEvent candidate in eligible)
        {
            roll -= candidate.Weight;
            if (roll <= 0f) { picked = candidate; break; }
        }

        List<PTSoul> subjects = partyMembers.Where(m => string.IsNullOrEmpty(picked.requiresTrait) ||
                                                        (m.employment != null && m.employment.HasTrait(picked.requiresTrait))).ToList();
        seenEvents.Add(picked.id);
        PendingEvent = picked;
        EventSubject = subjects[Random.Range(0, subjects.Count)];
    }

    bool EventEligible(PTNightEvent nightEvent)
    {
        if (seenEvents.Contains(nightEvent.id)) return false;
        if (daysElapsed < nightEvent.minDay) return false;
        if (nightEvent.maxDay > 0 && daysElapsed > nightEvent.maxDay) return false;
        if (partyMembers.Count < Mathf.Max(1, nightEvent.minParty)) return false;
        if (!string.IsNullOrEmpty(nightEvent.requiresTrait) &&
            !partyMembers.Exists(m => m.employment != null && m.employment.HasTrait(nightEvent.requiresTrait))) return false;
        return true;
    }

    public string EventText(string text) => text == null ? "" : text.Replace("{name}", EventSubject != null ? EventSubject.Name : "someone");

    public bool CanChooseEventOption(int index)                                                              //a choice that costs gold needs the gold
    {
        if (PendingEvent == null || index < 0 || index >= PendingEvent.choices.Length) return false;
        return gold >= PendingEvent.choices[index].GoldCost;
    }

    public void ResolveEvent(int index)                                                                      //the player picks a choice
    {
        if (PendingEvent == null || !CanChooseEventOption(index)) return;
        PTEventChoice choice = PendingEvent.choices[index];
        PTSoul subject = EventSubject;
        PTAdventureLog.Log(PendingEvent.title + ": " + EventText(choice.label) + ".");
        PendingEvent = null;
        EventSubject = null;

        PTEventEffect outcome = Random.value < choice.Chance ? choice.success : choice.failure;
        if (outcome != null) ApplyEventEffect(outcome, subject);
        UpdateUI();
    }

    void ApplyEventEffect(PTEventEffect effect, PTSoul subject)
    {
        if (!string.IsNullOrEmpty(effect.resultText))
        {
            PTAdventureLog.Log(effect.resultText.Replace("{name}", subject != null ? subject.Name : "someone"));
        }
        int goldChange = effect.gold + Mathf.FloorToInt(gold * effect.goldPercent / 100f);
        if (goldChange != 0) ChangeGold(goldChange);
        if (effect.partyLoyalty != 0) ChangePartyLoyalty(effect.partyLoyalty);
        if (effect.partyHealPercent != 0)
        {
            foreach (PTSoul member in partyMembers) ChangeHealthPercent(member, effect.partyHealPercent);
        }
        TributeAdjustment += effect.tributeChange;
        NextJobPayPercent += effect.nextJobPayPercent;

        if (subject == null || !partyMembers.Contains(subject)) return;
        if (effect.subjectLoyalty != 0) ChangeLoyalty(subject, effect.subjectLoyalty);
        if (effect.subjectHealPercent != 0) ChangeHealthPercent(subject, effect.subjectHealPercent);
        if (effect.subjectCowardly > 0 && !subject.markedByDeath) subject.isCowardly = true;
        if (effect.subjectCowardly < 0) subject.isCowardly = false;
        if (subject.employment != null && effect.subjectGreed != 0f) subject.employment.greed = Mathf.Clamp01(subject.employment.greed + effect.subjectGreed);
        if (effect.subjectMight != 0) subject.ChangeAttribute(SoulAttribute.Might, effect.subjectMight);
        if (effect.subjectConstitution != 0) subject.ChangeAttribute(SoulAttribute.Constitution, effect.subjectConstitution);
        if (effect.subjectXp > 0) subject.GainXP(effect.subjectXp);
        if (effect.subjectWagePercent != 0)
        {
            subject.baseWage = Mathf.Max(1, Mathf.RoundToInt(subject.baseWage * (1f + effect.subjectWagePercent / 100f)));
            subject.RecalculateWage();
        }
        if (effect.subjectLeaves)
        {
            RemovePartyMember(subject, EmploymentStatus.Quit, "Left for a rival company on day " + daysElapsed);
        }
    }

    static void ChangeHealthPercent(PTSoul member, int percent)                                              //events heal, or hurt without killing
    {
        int amount = Mathf.RoundToInt(member.maxHP * percent / 100f);
        if (amount > 0) member.RestoreHP(amount);
        else
        {
            member.currentHP = Mathf.Max(1, member.currentHP + amount);
            member.UpdateUI();
        }
    }
#endregion

#region Raise negotiations
    public PTSoul PendingRaise => partyMembers.FirstOrDefault(m => m.employment != null && !m.employment.OnLootShare &&
                                                                    m.employment.requestedWage > m.dailywages);

    public float CounterChance(PTSoul member) => member == null ? 0f :
        Mathf.Clamp(COUNTER_BASE_CHANCE - (member.atrSense - 3) * COUNTER_CHANCE_PER_SENSE, COUNTER_MIN_CHANCE, COUNTER_MAX_CHANCE);

    public void ResolveRaise(RaiseChoice choice)                                                             //answer the first pending raise request
    {
        PTSoul member = PendingRaise;
        if (member == null) return;
        PTEmployeeRecord job = member.employment;
        int asked = job.requestedWage;
        job.requestedWage = 0;

        switch (choice)
        {
            case RaiseChoice.Accept:
                PTAdventureLog.Log("You give " + member.Name + " their raise: " + member.dailywages + " to " + asked + " gold a day.");
                member.dailywages = asked;
                job.ChangeLoyalty(RAISE_ACCEPT_LOYALTY);
                break;
            case RaiseChoice.Refuse:
                PTAdventureLog.Log("You refuse " + member.Name + "'s raise. They stay on " + member.dailywages + " gold, and they're not happy about it.");
                job.ChangeLoyalty(RAISE_REFUSE_LOYALTY);
                break;
            case RaiseChoice.Counter:
                if (Random.value < CounterChance(member))
                {
                    int halfway = (member.dailywages + asked) / 2;
                    PTAdventureLog.Log("You haggle " + member.Name + " down to " + halfway + " gold a day. Fair enough, they say.");
                    member.dailywages = halfway;
                }
                else
                {
                    PTAdventureLog.Log(member.Name + " is insulted by your counter-offer and holds out for the full " + asked + " gold.");
                    member.dailywages = asked;
                    job.ChangeLoyalty(RAISE_COUNTER_FAIL_LOYALTY);
                }
                break;
        }
        UpdateUI();
    }
#endregion
}
