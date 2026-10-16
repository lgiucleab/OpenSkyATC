namespace OpenSkyAtc.Core.Geo;

public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>Great-circle helpers on a spherical Earth.</summary>
public static class GeoMath
{
    public const double EarthRadiusM = 6_371_008.8;
    public const double MetresPerNm = 1852.0;

    public static double DistanceM(GeoPoint a, GeoPoint b)
    {
        double lat1 = Rad(a.Latitude), lat2 = Rad(b.Latitude);
        double dLat = lat2 - lat1, dLon = Rad(AngleDiffDeg(b.Longitude, a.Longitude));
        double h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static double DistanceNm(GeoPoint a, GeoPoint b) => DistanceM(a, b) / MetresPerNm;

    /// <summary>Initial true bearing from a to b, in [0, 360).</summary>
    public static double BearingDeg(GeoPoint a, GeoPoint b)
    {
        double lat1 = Rad(a.Latitude), lat2 = Rad(b.Latitude), dLon = Rad(AngleDiffDeg(b.Longitude, a.Longitude));
        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return NormalizeDeg(Deg(Math.Atan2(y, x)));
    }

    /// <summary>Point reached from <paramref name="start"/> along a true bearing.</summary>
    public static GeoPoint Destination(GeoPoint start, double bearingDeg, double distanceM)
    {
        double d = distanceM / EarthRadiusM, brg = Rad(bearingDeg), lat1 = Rad(start.Latitude), lon1 = Rad(start.Longitude);
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brg));
        double lon2 = lon1 + Math.Atan2(Math.Sin(brg) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        return new GeoPoint(Deg(lat2), AngleDiffDeg(Deg(lon2), 0));
    }

    /// <summary>
    /// Position of <paramref name="p"/> relative to the great circle from <paramref name="a"/> to <paramref name="b"/>:
    /// signed cross-track distance (positive to the right) and along-track distance from <paramref name="a"/>, in nm.
    /// Along-track is negative behind <paramref name="a"/>.
    /// </summary>
    public static (double CrossNm, double AlongNm) TrackOffsets(GeoPoint a, GeoPoint b, GeoPoint p)
    {
        double d13 = DistanceM(a, p) / EarthRadiusM;
        double dt = Rad(BearingDeg(a, p) - BearingDeg(a, b));
        double xt = Math.Asin(Math.Sin(d13) * Math.Sin(dt));
        double along = Math.Acos(Math.Clamp(Math.Cos(d13) / Math.Cos(xt), -1, 1)) * Math.Sign(Math.Cos(dt));
        return (xt * EarthRadiusM / MetresPerNm, along * EarthRadiusM / MetresPerNm);
    }

    /// <summary>Angle in [0, 360).</summary>
    public static double NormalizeDeg(double deg)
    {
        double r = deg % 360;
        return r < 0 ? r + 360 : r;
    }

    /// <summary>Signed difference a - b, in (-180, 180].</summary>
    public static double AngleDiffDeg(double a, double b)
    {
        double d = NormalizeDeg(a - b);
        return d > 180 ? d - 360 : d;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
    private static double Deg(double rad) => rad * 180 / Math.PI;
}

/// <summary>
/// Flat east/north frame in metres around an origin. Accurate to well under a metre within a few km,
/// which is all that runway and taxiway geometry needs.
/// </summary>
public readonly struct LocalFrame(GeoPoint origin)
{
    private readonly double _metresPerDegLat = GeoMath.EarthRadiusM * Math.PI / 180;
    private readonly double _metresPerDegLon = GeoMath.EarthRadiusM * Math.PI / 180 * Math.Cos(origin.Latitude * Math.PI / 180);

    public GeoPoint Origin { get; } = origin;

    public (double East, double North) ToLocal(GeoPoint p) =>
        (GeoMath.AngleDiffDeg(p.Longitude, Origin.Longitude) * _metresPerDegLon, (p.Latitude - Origin.Latitude) * _metresPerDegLat);
}
