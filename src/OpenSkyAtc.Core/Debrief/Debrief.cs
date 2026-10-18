using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Debrief;

/// <summary>How bad a finding is, most serious first (the debrief groups by this).</summary>
public enum FindingSeverity
{
    /// <summary>Runway incursion, takeoff or landing without clearance.</summary>
    Serious,

    /// <summary>A conformance check that reached a logged deviation (level 3).</summary>
    Deviation,

    /// <summary>A conformance reminder or instruction the pilot then followed (level 1-2).</summary>
    Warning,

    Readback,
    Handoff,

    /// <summary>Worth knowing, no penalty (a go-around).</summary>
    Info,
}

public enum FindingKind
{
    Conformance,
    ReadbackWrong,
    ReadbackMissed,
    HandoffLate,
    HandoffMissed,
    HandoffSkipped,
    GoAround,
}

/// <summary>
/// One thing the debrief reports. Structured only (Core never builds display text): the app words it in the
/// user's language. Fields not used by a kind are null.
/// </summary>
/// <param name="Penalty">Points taken off the score.</param>
/// <param name="Tag">For readbacks: which instruction ("ifr-clearance", "taxi", "contact-tower"...).</param>
/// <param name="Station">For handoffs: the unit to call; for a skipped handoff, the unit called instead is in <paramref name="Other"/>.</param>
public sealed record DebriefFinding(
    TimeSpan Time,
    FindingKind Kind,
    FindingSeverity Severity,
    int Penalty,
    ConformanceCheck? Check = null,
    int? Level = null,
    double? Worst = null,
    string? Unit = null,
    string? Tag = null,
    IReadOnlyList<ReadbackItem>? Wrong = null,
    IReadOnlyList<ReadbackItem>? Missing = null,
    string? Station = null,
    string? Other = null,
    double? Seconds = null,
    FlightPhase? Phase = null);

/// <summary>A handoff and how long the pilot took to call the new unit (null: never called).</summary>
/// <param name="Timed">Airborne handoffs are timed against the limit; ground ones ("contact Ground" after landing) are only listed.</param>
public sealed record HandoffTiming(TimeSpan Time, string From, string To, int FrequencyKhz, double? Seconds, bool Timed);

/// <summary>The end-of-flight debrief (step 16), built from the session log by <see cref="DebriefBuilder"/>.</summary>
public sealed record FlightDebrief
{
    public int Version { get; init; } = 1;
    public string Callsign { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Destination { get; init; } = "";
    public string? AircraftType { get; init; }

    /// <summary>When the flight ended, in real time (the host sets it; Core has no clock).</summary>
    public DateTimeOffset? EndedAt { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>True when the flight ended parked with the engines off; false when it was ended early.</summary>
    public bool Completed { get; init; }
    public FlightPhase LastPhase { get; init; }

    public int Score { get; init; }
    public IReadOnlyList<DebriefFinding> Findings { get; init; } = [];

    /// <summary>Instructions that needed a readback, and how many were right the first time.</summary>
    public int Readbacks { get; init; }
    public int ReadbacksCorrect { get; init; }
    public IReadOnlyList<HandoffTiming> Handoffs { get; init; } = [];
}

/// <summary>Score weights and limits (step 16 decisions).</summary>
public sealed record DebriefOptions
{
    public int Serious { get; init; } = 20;
    public int Deviation { get; init; } = 10;
    public int WarningLevel2 { get; init; } = 4;
    public int WarningLevel1 { get; init; } = 2;
    public int ReadbackWrong { get; init; } = 2;
    public int ReadbackMissed { get; init; } = 3;
    public int HandoffLate { get; init; } = 3;
    public int HandoffMissed { get; init; } = 8;
    public int HandoffSkipped { get; init; } = 5;

    /// <summary>An airborne handoff answered later than this is late (the conformance grace, design doc: 45 s).</summary>
    public TimeSpan LateHandoff { get; init; } = TimeSpan.FromSeconds(45);
}

/// <summary>
/// Builds the debrief from the session log: conformance episodes by severity, readback errors and misses, handoff
/// timings, go-arounds, and a score out of 100 (100 minus the penalties, never below 0).
/// </summary>
public static class DebriefBuilder
{
    private static readonly FlightPhase[] AirbornePhases =
        [FlightPhase.InitialClimb, FlightPhase.Departure, FlightPhase.EnRoute, FlightPhase.Descent, FlightPhase.Approach, FlightPhase.Final];

    public static FlightDebrief Build(FlightSession session, FlightPlan plan, DebriefOptions? options = null)
    {
        var o = options ?? new DebriefOptions();
        var entries = session.Log.Entries;
        var findings = new List<DebriefFinding>();

        // Conformance episodes (missed handoffs are reported with the handoff timings instead).
        foreach (var e in entries.OfType<ConformanceEpisode>().Where(e => e.Check != ConformanceCheck.MissedHandoff))
        {
            var (severity, penalty) = e.Serious ? (FindingSeverity.Serious, o.Serious)
                : e.IsDeviation ? (FindingSeverity.Deviation, o.Deviation)
                : (FindingSeverity.Warning, e.MaxLevel >= 2 ? o.WarningLevel2 : o.WarningLevel1);
            findings.Add(new DebriefFinding(e.Start, FindingKind.Conformance, severity, penalty,
                Check: e.Check, Level: e.MaxLevel, Worst: e.Worst, Unit: e.Unit, Phase: PhaseAt(entries, e.Start)));
        }

        // Readbacks: each instruction counts once, judged by its first readback.
        var checks = entries.OfType<ReadbackChecked>().GroupBy(c => c.ExchangeId).ToDictionary(g => g.Key, g => g.First());
        var missed = entries.OfType<ReadbackMissed>().GroupBy(m => m.ExchangeId).ToDictionary(g => g.Key, g => g.First());
        var exchanges = checks.Keys.Union(missed.Keys).ToList();
        int correct = 0;
        foreach (int id in exchanges)
        {
            if (missed.TryGetValue(id, out var miss))
            {
                findings.Add(new DebriefFinding(miss.Time, FindingKind.ReadbackMissed, FindingSeverity.Readback, o.ReadbackMissed, Tag: miss.Tag));
                continue;
            }
            var first = checks[id];
            if (first.Verdict.IsCorrect) correct++;
            else findings.Add(new DebriefFinding(first.Time, FindingKind.ReadbackWrong, FindingSeverity.Readback, o.ReadbackWrong,
                Tag: first.Tag, Wrong: first.Verdict.Wrong, Missing: first.Verdict.Missing));
        }

        // Handoffs: the first "contact X" to each unit, timed to the pilot's first call to X.
        var handoffs = new List<HandoffTiming>();
        var pilotCalls = entries.OfType<PilotIntentLogged>().Where(p => p.Unit is not null).ToList();
        var seen = new HashSet<ControllerUnit>();
        foreach (var t in entries.OfType<AtcTransmissionLogged>())
        {
            foreach (var contact in t.Message.Parts.OfType<Contact>())
            {
                if (contact.Condition != ContactCondition.None || contact.Unit == t.Message.Unit || !seen.Add(contact.Unit)) continue;
                var time = t.Message.Time;
                var call = pilotCalls.FirstOrDefault(p => p.Time >= time && p.Unit == contact.Unit);
                double? seconds = call is null ? null : (call.Time - time).TotalSeconds;
                bool timed = AirbornePhases.Contains(PhaseAt(entries, time));
                handoffs.Add(new HandoffTiming(time, t.Message.Unit.Callsign, contact.Unit.Callsign, contact.Unit.FrequencyKhz, seconds, timed));
                if (!timed) continue;
                if (seconds is null)
                    findings.Add(new DebriefFinding(time, FindingKind.HandoffMissed, FindingSeverity.Handoff, o.HandoffMissed, Station: contact.Unit.Callsign));
                else if (seconds > o.LateHandoff.TotalSeconds)
                    findings.Add(new DebriefFinding(time, FindingKind.HandoffLate, FindingSeverity.Handoff, o.HandoffLate, Station: contact.Unit.Callsign, Seconds: seconds));
            }
        }
        foreach (var skip in entries.OfType<HandoffSkipped>())
            findings.Add(new DebriefFinding(skip.Time, FindingKind.HandoffSkipped, FindingSeverity.Handoff, o.HandoffSkipped,
                Station: skip.Expected.Callsign, Other: skip.Called.Callsign));

        foreach (var g in entries.OfType<GoAroundLogged>())
            findings.Add(new DebriefFinding(g.Time, FindingKind.GoAround, FindingSeverity.Info, 0, Phase: g.Phase));

        var ordered = findings.OrderBy(f => f.Severity).ThenBy(f => f.Time).ToList();
        return new FlightDebrief
        {
            Callsign = plan.Callsign,
            Origin = plan.Origin,
            Destination = plan.Destination,
            AircraftType = plan.AircraftType,
            Duration = session.SimTime,
            Completed = session.Ended,
            LastPhase = session.Phase,
            Score = Math.Max(0, 100 - ordered.Sum(f => f.Penalty)),
            Findings = ordered,
            Readbacks = exchanges.Count,
            ReadbacksCorrect = correct,
            Handoffs = handoffs,
        };
    }

    /// <summary>The flight phase at <paramref name="time"/>, from the phase changes in the log.</summary>
    private static FlightPhase PhaseAt(IReadOnlyList<LogEntry> entries, TimeSpan time)
    {
        var phase = FlightPhase.Preflight;
        foreach (var e in entries)
        {
            if (e.Time > time) break;
            if (e is PhaseChanged c) phase = c.To;
            else if (e is PhaseInferred i) phase = i.Phase;
        }
        return phase;
    }
}
