using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Config;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Tests.Config;

public class DataFilesTests
{
    private static string Repo(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "data", file);
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(file);
    }

    [Fact]
    public void ShippedFiles_ParseAndAreEmpty()
    {
        Assert.Empty(DataFiles.ParseSquawks(Repo("squawk-ranges.json")).Ranges);
        Assert.Empty(DataFiles.ParseSops(Repo("airport-sops.json")).Airports);
        Assert.Empty(DataFiles.ParseCtrSectors(Repo("ctr-frequencies.json")));
    }

    [Fact]
    public void Squawks_AreOctalCodes()
    {
        var c = DataFiles.ParseSquawks("""{ "ranges": [ { "icaoPrefix": "LE", "first": "3401", "last": "3477" } ] }""");
        Assert.Equal(new OpenSkyAtc.Core.Planning.SquawkRange("LE", 3401, 3477), Assert.Single(c.Ranges));
        Assert.Throws<FormatException>(() => DataFiles.ParseSquawks("""{ "ranges": [ { "icaoPrefix": "LE", "first": "3481", "last": "3477" } ] }"""));
    }

    [Fact]
    public void Sops_AndCtrSectors_Parse()
    {
        var sops = DataFiles.ParseSops("""{ "airports": [ { "icao": "LEBL", "preferredRunways": ["24L", "24R"] } ] }""");
        Assert.Equal(["24L", "24R"], sops.PreferredFor("lebl"));

        var ctr = Assert.Single(DataFiles.ParseCtrSectors("""{ "sectors": [ { "name": "Madrid Control", "frequencyMhz": 133.1, "latitude": 40, "longitude": -3.7 } ] }"""));
        Assert.Equal(133_100, ctr.FrequencyKhz);
        Assert.Equal("Madrid Control", AirspaceModel.SectorName(ctr));
        Assert.Throws<FormatException>(() => DataFiles.ParseCtrSectors("""{ "sectors": [ { "name": "X", "frequencyMhz": 13.31, "latitude": 0, "longitude": 0 } ] }"""));
        Assert.Throws<FormatException>(() => DataFiles.ParseSops("{ not json"));
    }
}

public class CenterSearchTests
{
    private static readonly AirportData A = new() { Icao = "XAAA", Location = new GeoPoint(40, 0) };
    private static readonly AirportData B = new() { Icao = "XBBB", Location = new GeoPoint(43, 0) };
    private static readonly FlightPlan Plan = new() { Callsign = "TST1", Origin = "XAAA", Destination = "XBBB" };

    [Fact]
    public void RoutePoints_AreAtMostTheStepApart_EndToEnd()
    {
        var points = CenterSearch.RoutePoints(A, B, Plan, stepNm: 50);
        Assert.True(GeoMath.DistanceNm(points[0], A.ReferencePoint!.Value) < 0.1);
        Assert.True(GeoMath.DistanceNm(points[^1], B.ReferencePoint!.Value) < 0.1);
        Assert.Equal(5, points.Count); // 180 nm: 0, 50, 100, 150, end
        Assert.All(points.Zip(points.Skip(1)), p => Assert.True(GeoMath.DistanceNm(p.First, p.Second) <= 50.01));
        Assert.Empty(CenterSearch.RoutePoints(new AirportData { Icao = "X" }, new AirportData { Icao = "Y" }, Plan));
    }

    [Fact]
    public void Merge_KeepsTheFileEntry_WhenTheSimHasTheSameFrequency()
    {
        var p = new GeoPoint(41, 0);
        var file = new CenterFrequency(133_100, p, "", "Madrid Control");
        var sim = new[] { new CenterFrequency(133_100, p, "LEMD", "MADRID CENTER"), new CenterFrequency(128_000, p, "LEMD") };
        var merged = CenterSearch.Merge(sim, [file]);
        Assert.Equal(2, merged.Count);
        Assert.Same(file, merged[0]);
    }
}
