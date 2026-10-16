using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;

namespace OpenSkyAtc.Core.Phases;

/// <summary>
/// Works out the phase from the aircraft's situation, for a session that starts away from the gate or after a
/// teleport (design doc edge cases). In the air it also issues a fresh clearance to the current altitude.
/// </summary>
public static class PhaseInference
{
    public static (FlightPhase Phase, string Reason) Infer(PhaseContext ctx)
    {
        var s = ctx.State;
        var pos = ctx.Position;
        double toOrigin = ctx.Origin.ReferencePoint is { } o ? GeoMath.DistanceNm(pos, o) : double.MaxValue;
        double toDestination = ctx.Destination.ReferencePoint is { } d ? GeoMath.DistanceNm(pos, d) : double.MaxValue;
        bool onRunway = ctx.Events.CurrentRunway is not null;

        if (s.OnGround)
        {
            bool atOrigin = toOrigin <= toDestination;
            return (atOrigin, onRunway) switch
            {
                (true, true) => (FlightPhase.Holding, "on a runway at the origin"),
                (true, false) => (FlightPhase.Preflight, "on the ground at the origin"),
                (false, true) => (FlightPhase.LandingRoll, "on a runway at the destination"),
                (false, false) => (FlightPhase.TaxiIn, "on the ground at the destination"),
            };
        }

        if (ctx.Airspace.InArrivalVolume(pos, s.IndicatedAltitudeFt) && toDestination < toOrigin)
            return (FlightPhase.Approach, "airborne near the destination");
        if (ctx.Airspace.InDepartureVolume(pos, s.IndicatedAltitudeFt))
            return (FlightPhase.Departure, "airborne near the origin");
        return (FlightPhase.EnRoute, "airborne en route");
    }

    /// <summary>Sets up the clearance the inferred phase needs: altitude and squawk in the air, the runway on a runway.</summary>
    public static void Prepare(PhaseContext ctx, FlightPhase phase)
    {
        var s = ctx.State;
        var c = ctx.Clearance;
        if (!s.OnGround)
        {
            var nearest = phase == FlightPhase.Approach ? ctx.Destination : ctx.Origin;
            c.ClearedAltitude = ctx.Planning.CurrentLevel(s.IndicatedAltitudeFt, nearest);
            c.Squawk ??= SquawkAllocator.Allocate(ctx.Options.Squawks, ctx.Origin.Icao, ctx.Rng);
            c.AssignedHeadingDeg = null;
        }
        else if (ctx.Events.CurrentRunway is { } runway)
        {
            var end = Math.Abs(GeoMath.AngleDiffDeg(s.HeadingTrueDeg, runway.First.HeadingTrueDeg)) <= 90 ? runway.First : runway.Second;
            c.Runway = end.Designator;
        }
        c.ClearHandoff();
    }
}
