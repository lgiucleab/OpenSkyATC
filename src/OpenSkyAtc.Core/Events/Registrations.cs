using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Events;

public enum AltitudeReference
{
    /// <summary>Indicated (barometric) altitude.</summary>
    Indicated,

    /// <summary>Height above ground level.</summary>
    AboveGround,
}

/// <summary>
/// Something a phase asked the engine to watch. Phases register in OnEnter and clear in OnExit
/// (<see cref="EventEngine.UnregisterAll"/> with the phase as owner).
/// </summary>
public abstract class EventRegistration(object owner, string? tag)
{
    public object Owner { get; } = owner;

    /// <summary>Free text for the owner to recognise the event, e.g. "handoff-departure".</summary>
    public string? Tag { get; } = tag;

    internal abstract void Reset();
}

public sealed class AltitudeRegistration : EventRegistration
{
    internal AltitudeRegistration(object owner, double feet, AltitudeReference reference, double bandFt, string? tag)
        : base(owner, tag)
    {
        Feet = feet;
        Reference = reference;
        Crossing = new ThresholdCrossing(feet, bandFt);
    }

    public double Feet { get; }
    public AltitudeReference Reference { get; }
    internal ThresholdCrossing Crossing { get; }

    internal override void Reset() => Crossing.Reset();
}

public sealed class DistanceRegistration : EventRegistration
{
    internal DistanceRegistration(object owner, GeoPoint point, double nm, double bandNm, string? tag)
        : base(owner, tag)
    {
        Point = point;
        DistanceNm = nm;
        Crossing = new ThresholdCrossing(nm, bandNm);
    }

    public GeoPoint Point { get; }
    public double DistanceNm { get; }
    internal ThresholdCrossing Crossing { get; }

    internal override void Reset() => Crossing.Reset();
}

/// <summary>
/// Watches for the aircraft established on the final approach course to a runway end.
/// Unlike crossings, this is a state: if the aircraft is already established when registered, it fires.
/// </summary>
public sealed class EstablishedRegistration : EventRegistration
{
    internal EstablishedRegistration(object owner, RunwayEnd runwayEnd, double courseTrueDeg, double? maxDistanceNm, EventEngineOptions o, string? tag)
        : base(owner, tag)
    {
        RunwayEnd = runwayEnd;
        CourseTrueDeg = courseTrueDeg;
        MaxDistanceNm = maxDistanceNm;
        Trigger = new HeldTrigger(o.EstablishedHold, o.EstablishedRearmHold, startArmed: true);
    }

    public RunwayEnd RunwayEnd { get; }

    /// <summary>Final approach course, true degrees (the direction of landing).</summary>
    public double CourseTrueDeg { get; }

    public double? MaxDistanceNm { get; }
    internal HeldTrigger Trigger { get; }
    internal double? PreviousDistanceM { get; set; }

    internal override void Reset()
    {
        Trigger.Reset(armed: true);
        PreviousDistanceM = null;
    }
}
