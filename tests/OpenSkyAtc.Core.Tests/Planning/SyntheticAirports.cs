using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Tests.Planning;

/// <summary>
/// Hand-made airports in the sim's facility format, copying the real data's quirks (taxiways as path type 4,
/// hold-shorts as type 5, apron lanes as type 6, parking paths from a taxi point to a parking index).
/// </summary>
/// <remarks>
/// XGRD layout, metres east/north of 45N 0E:
///   stands 11 (1000, 400) and 12 (2000, 400), lead-in lanes down to taxiway A (y = 150, x 0..3100);
///   runway 09/27 along y = 0, x 0..3000;
///   connector B at x = 1500 crosses 09/27 (hold-shorts at y = +60 and -60) down to C (y = -200);
///   C runs to the hold-short of runway 18 (1500, -260); runway 18/36 runs south from (1500, -300);
///   D goes round the east end of 09/27 (x = 3100) to E (y = -200) back to C: no crossing, but much longer.
/// </remarks>
internal static class SyntheticAirports
{
    public static readonly GeoPoint Origin = new(45, 0);

    public static GeoPoint At(double east, double north) =>
        GeoMath.Destination(GeoMath.Destination(Origin, 90, east), 0, north);

    private sealed class Builder
    {
        public readonly List<FacilityTaxiPoint> Points = [];
        public readonly List<FacilityTaxiPath> Paths = [];
        public readonly List<FacilityParking> Parkings = [];
        public readonly List<string> Names = [""];
        private readonly bool _unnamed;

        public Builder(bool unnamed) => _unnamed = unnamed;

        public int Point(double e, double n, int type = 1)
        {
            var p = At(e, n);
            Points.Add(new FacilityTaxiPoint(Points.Count, type, 0, p.Latitude, p.Longitude));
            return Points.Count - 1;
        }

        public void Path(int a, int b, string? name, int type = 4)
        {
            uint index = 0;
            if (name is not null && !_unnamed)
            {
                if (!Names.Contains(name)) Names.Add(name);
                index = (uint)Names.IndexOf(name);
            }
            Paths.Add(new FacilityTaxiPath(type, 20, 0, 0, a, b, index));
        }

        /// <summary>A taxiway as a chain of points, so it has several edges like real data.</summary>
        public void Chain(string? name, params int[] points)
        {
            for (int i = 0; i + 1 < points.Length; i++) Path(points[i], points[i + 1], name);
        }

        public void Stand(int number, double e, double n, int leadInPoint)
        {
            var p = At(e, n);
            Parkings.Add(new FacilityParking(Parkings.Count, 9, 10, 0, (uint)number, 180, 25, p.Latitude, p.Longitude));
            Paths.Add(new FacilityTaxiPath(3, 20, 0, 0, leadInPoint, Parkings.Count - 1, 0));
        }
    }

    public static FacilityDump Grid(bool unnamed = false, string icao = "XGRD")
    {
        var b = new Builder(unnamed);
        // Taxiway A, west to east, with nodes where things join.
        int a0 = b.Point(0, 150), a1 = b.Point(1000, 150), a2 = b.Point(1500, 150), a3 = b.Point(2000, 150), a4 = b.Point(3100, 150);
        b.Chain("A", a0, a1, a2, a3, a4);

        // Stand lead-ins (apron lanes, unnamed) and stands.
        int l1 = b.Point(1000, 300), l2 = b.Point(2000, 300);
        b.Path(a1, l1, null, type: 6);
        b.Path(a3, l2, null, type: 6);
        b.Stand(11, 1000, 400, l1);
        b.Stand(12, 2000, 400, l2);

        // Connector B across runway 09/27, with an unnamed 40 m piece in the middle of it (a naming gap).
        int bh1 = b.Point(1500, 60, type: 5), bmid1 = b.Point(1500, 20), bmid2 = b.Point(1500, -20), bh2 = b.Point(1500, -60, type: 5), c0 = b.Point(1500, -200);
        b.Chain("B", a2, bh1, bmid1);
        b.Path(bmid1, bmid2, null);
        b.Chain("B", bmid2, bh2, c0);

        // C to the hold-short of runway 18.
        int h18 = b.Point(1500, -260, type: 5);
        b.Path(c0, h18, "C");

        // D and E: around the east end of 09/27, no crossing.
        int d1 = b.Point(3100, -200), e1 = b.Point(2300, -200);
        b.Path(a4, d1, "D");
        b.Chain("E", d1, e1, c0);

        // Runway 09/27 centreline as runway paths (should not be used for taxiing).
        int r0 = b.Point(0, 0), r1 = b.Point(1500, 0), r2 = b.Point(3000, 0);
        b.Paths.Add(new FacilityTaxiPath(2, 45, 9, 0, r0, r1, 0));
        b.Paths.Add(new FacilityTaxiPath(2, 45, 9, 0, r1, r2, 0));

        // A service road the router must ignore: a straight shortcut from stand 11's lane to the runway 18 hold-short.
        b.Path(l1, h18, null, type: 7);

        var c09 = At(1500, 0);
        var c18 = At(1500, -1550);
        return new FacilityDump
        {
            Icao = icao,
            Name = "Gridtown Intl",
            Latitude = Origin.Latitude,
            Longitude = Origin.Longitude,
            ElevationM = 100,
            TransitionAltitudeM = 1828.8, // 6,000 ft
            Runways =
            [
                new FacilityRunway(c09.Latitude, c09.Longitude, 100, 90, 3000, 45, 9, 0, 27, 0, null, null),
                new FacilityRunway(c18.Latitude, c18.Longitude, 100, 180, 2500, 45, 18, 0, 36, 0, "IGR", null),
            ],
            Frequencies =
            [
                new FacilityFrequency(7, 121_850_000, "GRIDTOWN"),
                new FacilityFrequency(5, 121_700_000, "GRIDTOWN"),
                new FacilityFrequency(6, 118_100_000, "GRIDTOWN"),
                new FacilityFrequency(9, 124_700_000, "GRID CITY"),
                new FacilityFrequency(10, 132_350_000, "GRID CENTER"),
            ],
            TaxiPoints = b.Points,
            TaxiPaths = b.Paths,
            TaxiNames = b.Names,
            Parkings = b.Parkings,
            Approaches = [new FacilityApproach(4, 0, 18, 0, "FGR18", 914.4, 3000)],
            Departures = ["GRID1A"],
            Arrivals = ["GRID1B"],
        };
    }

    /// <summary>
    /// XTHR: runway 18/36 from (0, 0) north to (0, 3000), approached from the east along taxiway Y (x = 150).
    /// The hold-short nearest the 36 threshold (82 m from it) is on the far, west side, reachable only via Z, which
    /// passes 30 m behind the threshold; the near-side hold-short on E1 is 100 m from it.
    /// </summary>
    public static FacilityDump ThresholdShortcut()
    {
        var b = new Builder(unnamed: false);
        int y0 = b.Point(150, 600), y1 = b.Point(150, 100), y2 = b.Point(150, -30);
        b.Chain("Y", y0, y1, y2);
        int e1 = b.Point(80, 100, type: 5);
        b.Path(y1, e1, "E1");
        int z1 = b.Point(0, -30), z2 = b.Point(-80, -30), farHold = b.Point(-80, 20, type: 5);
        b.Chain("Z", y2, z1, z2, farHold);
        int stand = b.Point(300, 600);
        b.Path(stand, y0, "A");

        var centre = At(0, 1500);
        return new FacilityDump
        {
            Icao = "XTHR",
            Name = "Threshold",
            Latitude = Origin.Latitude,
            Longitude = Origin.Longitude,
            ElevationM = 100,
            Runways = [new FacilityRunway(centre.Latitude, centre.Longitude, 100, 0, 3000, 45, 36, 0, 18, 0, null, null)],
            Frequencies = [new FacilityFrequency(5, 121_700_000, "THRESHOLD")],
            TaxiPoints = b.Points,
            TaxiPaths = b.Paths,
            TaxiNames = b.Names,
            Parkings = b.Parkings,
        };
    }

    public static FacilityDump NoTaxiNetwork() => Grid() with { Icao = "XNON", TaxiPoints = [], TaxiPaths = [], Parkings = [], TaxiNames = [] };
}
