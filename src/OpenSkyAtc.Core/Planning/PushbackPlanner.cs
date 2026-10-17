using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Planning;

/// <summary>
/// How to push back (owner's request): straight back from the stand by <see cref="StraightBackM"/>, then turn so
/// the nose ends on <see cref="FinalHeadingTrueDeg"/>, the direction of the first leg of the departure taxi route,
/// said by ATC as "facing <see cref="Facing"/>".
/// </summary>
public sealed record PushPlan(GeoPoint Start, double StartHeadingTrueDeg, double StraightBackM, double FinalHeadingTrueDeg, string Facing);

public static class PushbackPlanner
{
    /// <summary>Straight push before turning, when the stand's taxi node does not say how far the lane is.</summary>
    public const double DefaultStraightM = 25;

    private const double MinStraightM = 8, MaxStraightM = 80;

    /// <summary>
    /// The push for an aircraft at <paramref name="position"/> with nose on <paramref name="headingTrueDeg"/>; null when
    /// it is not at a stand (no pushback there). The aircraft ends sideways on the taxilane, facing the way the taxi
    /// route to the departure runway goes.
    /// </summary>
    public static PushPlan? Plan(AirportData airport, GeoPoint position, double headingTrueDeg, RunwayEnd? runway, ITaxiPlanner planner)
    {
        if (airport.Taxi is not { } graph) return null;
        var stand = graph.Stands
            .Select(s => (Stand: s, M: GeoMath.DistanceM(s.Position, position)))
            .Where(x => x.M <= Math.Max(x.Stand.RadiusM, 15) + 15)
            .OrderBy(x => x.M)
            .Select(x => x.Stand)
            .FirstOrDefault();
        if (stand is null) return null;

        // Straight back until level with the taxi node the stand connects to (the taxilane), measured behind the nose.
        double back = (headingTrueDeg + 180) % 360;
        // The stand is a node of its own; its parking path leads to the taxi node on the taxilane.
        int lead = graph.From(stand.NodeId).Select(e => e.From == stand.NodeId ? e.To : e.From).FirstOrDefault(n => n != stand.NodeId, stand.NodeId);
        var node = graph.Nodes[lead].Position;
        double toNode = GeoMath.DistanceM(position, node);
        double offAxis = Math.Abs(GeoMath.AngleDiffDeg(GeoMath.BearingDeg(position, node), back));
        double straight = toNode > 1 && offAxis < 75 ? toNode * Math.Cos(offAxis * Math.PI / 180) : DefaultStraightM;
        straight = Math.Clamp(straight, MinStraightM, MaxStraightM);
        var end = GeoMath.Destination(position, back, straight);

        // Where the route goes from there (the first point at least 40 m on).
        double? routeDir = null;
        if (runway is not null)
        {
            var path = planner.PlanToHoldingPoint(airport, end, runway).Path;
            var ahead = path.FirstOrDefault(p => GeoMath.DistanceM(end, p) >= 40);
            if (ahead != default) routeDir = GeoMath.BearingDeg(end, ahead);
        }

        // A tug turns the aircraft sideways onto the taxilane, never round: the final heading is a taxilane direction
        // at the stand's node that lies roughly across the stand (45-135 degrees off the parked heading), the one
        // closest to the route's direction; without one, 90 degrees left or right, whichever is closer.
        var sideways = graph.From(lead)
            .Select(e => e.From == lead ? e.To : e.From)
            .Where(n => n != stand.NodeId && graph.Nodes[n].Kind != TaxiNodeKind.Parking)
            .Select(n => GeoMath.BearingDeg(node, graph.Nodes[n].Position))
            .Where(b => Math.Abs(GeoMath.AngleDiffDeg(b, headingTrueDeg)) is >= 45 and <= 135)
            .ToList();
        if (sideways.Count == 0) sideways = [(headingTrueDeg + 90) % 360, (headingTrueDeg + 270) % 360];
        double target = routeDir ?? sideways[0];
        double final = sideways.OrderBy(b => Math.Abs(GeoMath.AngleDiffDeg(b, target))).ThenBy(b => b).First();
        return new PushPlan(position, headingTrueDeg, straight, final, Compass(final));
    }

    /// <summary>Eight-point compass word for a heading: 0 north, 45 northeast...</summary>
    public static string Compass(double headingDeg) =>
        ((int)Math.Round(((headingDeg % 360) + 360) % 360 / 45) % 8) switch
        {
            0 => "north",
            1 => "northeast",
            2 => "east",
            3 => "southeast",
            4 => "south",
            5 => "southwest",
            6 => "west",
            _ => "northwest",
        };
}
