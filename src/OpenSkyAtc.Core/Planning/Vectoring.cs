using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Planning;

public enum VectorLeg
{
    /// <summary>Heading to join the downwind leg.</summary>
    ToDownwind,
    Downwind,
    Base,

    /// <summary>Lined up already: no vectors until the final clearance.</summary>
    StraightIn,

    /// <summary>The final vector and approach clearance have been given; no more vectors.</summary>
    Cleared,
}

/// <param name="HeadingMag">Heading to fly, degrees magnetic rounded to 5; null when no heading is needed.</param>
/// <param name="SpeedKt">Speed for this leg (jets only), or null.</param>
/// <param name="IsFinal">The final vector: send it with the approach clearance.</param>
/// <param name="AircraftSideRight">True when the aircraft is right of the final course (it closes the localiser from the right).</param>
public sealed record VectorStep(VectorLeg Leg, int? HeadingMag, int? SpeedKt, bool IsFinal, bool AircraftSideRight);

/// <summary>
/// Radar vectors to final (design doc "Approach", steps 1-5): a FAF 8-12 nm out, a downwind 5 nm off the
/// centreline on the aircraft's side, a base turn far enough out that the final vector intercepts at 20-30 degrees
/// at least 2 nm before the FAF, and speed control for jets (220 downwind, 180 base, 160 to 4 nm).
/// Works in the runway's frame: x = nm from the threshold outwards along the extended centreline,
/// y = nm right of the final course (looking in the landing direction).
/// </summary>
public sealed class Vectoring
{
    private readonly RunwayEnd _runway;
    private readonly PlanningOptions _o;
    private readonly double _fafNm, _interceptDeg;
    private readonly bool _jet;
    private int _side; // +1 right of final, -1 left

    public Vectoring(RunwayEnd runway, FlightPlanning planning, SimState s)
    {
        _runway = runway;
        _o = planning.Options;
        _fafNm = planning.FinalApproachFixNm;
        _interceptDeg = planning.InterceptAngleDeg;
        _jet = s.EngineType == EngineType.Jet;

        var (x, y) = Frame(new GeoPoint(s.Latitude, s.Longitude));
        _side = y >= 0 ? 1 : -1;
        // Straight in: roughly on the final course, near the extended centreline, and outside the base turn.
        bool alignedWithFinal = Math.Abs(GeoMath.AngleDiffDeg(s.HeadingTrueDeg, runway.HeadingTrueDeg)) <= 30
                                && Math.Abs(y) <= _o.DownwindOffsetNm;
        Leg = alignedWithFinal && x > BaseNm ? VectorLeg.StraightIn : VectorLeg.ToDownwind;
    }

    public VectorLeg Leg { get; private set; }
    public double FafNm => _fafNm;

    /// <summary>Distance from the threshold where base turns in: the intercept geometry from the lateral offset, plus 2 nm before the FAF.</summary>
    public double BaseNm => _fafNm + _o.InterceptBeforeFafNm + _o.FinalVectorLateralNm / Math.Tan(_interceptDeg * Math.PI / 180);

    /// <summary>The aircraft's position in the runway frame.</summary>
    public (double X, double Y) Frame(GeoPoint p)
    {
        var far = GeoMath.Destination(_runway.Position, _runway.HeadingTrueDeg + 180, 40 * GeoMath.MetresPerNm);
        var (cross, along) = GeoMath.TrackOffsets(far, _runway.Position, p);
        return (40 - along, cross);
    }

    /// <summary>The vector for now. Advances the leg as the aircraft gets there.</summary>
    public VectorStep Update(SimState s)
    {
        var (x, y) = Frame(new GeoPoint(s.Latitude, s.Longitude));
        double course = _runway.HeadingTrueDeg;

        switch (Leg)
        {
            case VectorLeg.ToDownwind when Math.Abs(y - _side * _o.DownwindOffsetNm) < 1.5:
                Leg = VectorLeg.Downwind;
                break;
            case VectorLeg.Downwind when x >= BaseNm:
                Leg = VectorLeg.Base;
                break;
            case VectorLeg.Base when Math.Abs(y) <= _o.FinalVectorLateralNm:
                return Final(s, y);
            case VectorLeg.StraightIn when x <= _fafNm + _o.StraightInClearanceBeyondFafNm:
                _side = y >= 0 ? 1 : -1;
                return Final(s, y);
        }

        double? headingTrue = Leg switch
        {
            // Join the downwind abeam where the aircraft is (between the threshold and the base turn).
            VectorLeg.ToDownwind => GeoMath.BearingDeg(new GeoPoint(s.Latitude, s.Longitude), FramePoint(Math.Clamp(x, 0, BaseNm - 1), _side * _o.DownwindOffsetNm)),
            VectorLeg.Downwind => course + 180,
            VectorLeg.Base => course - _side * 90,
            _ => null,
        };
        int? speed = !_jet ? null : Leg switch
        {
            VectorLeg.ToDownwind or VectorLeg.Downwind => _o.DownwindSpeedKt,
            VectorLeg.Base => _o.BaseSpeedKt,
            _ => null,
        };
        return new VectorStep(Leg, headingTrue is { } h ? Magnetic(s, h) : null, speed, false, _side > 0);
    }

    /// <summary>The final vector: a 20-30 degree intercept towards the centreline, or none when already on it.</summary>
    private VectorStep Final(SimState s, double y)
    {
        Leg = VectorLeg.Cleared;
        int? heading = Math.Abs(y) < 1 ? null : Magnetic(s, _runway.HeadingTrueDeg - _side * _interceptDeg);
        return new VectorStep(VectorLeg.Cleared, heading, _jet ? _o.FinalSpeedKt : null, true, _side > 0);
    }

    private GeoPoint FramePoint(double x, double y)
    {
        var onCentreline = GeoMath.Destination(_runway.Position, _runway.HeadingTrueDeg + 180, x * GeoMath.MetresPerNm);
        return GeoMath.Destination(onCentreline, _runway.HeadingTrueDeg + (y >= 0 ? 90 : -90), Math.Abs(y) * GeoMath.MetresPerNm);
    }

    /// <summary>True heading to magnetic (variation from the aircraft's headings), rounded to 5 degrees, 360 for north.</summary>
    public static int Magnetic(SimState s, double headingTrue)
    {
        double variation = GeoMath.AngleDiffDeg(s.HeadingTrueDeg, s.HeadingMagneticDeg);
        int h = (int)(Math.Round(GeoMath.NormalizeDeg(headingTrue - variation) / 5) * 5);
        return h is 0 or 360 ? 360 : h;
    }
}
