using System.Net;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.SimBrief;

namespace OpenSkyAtc.SimBrief.Tests;

internal static class Samples
{
    public static string Dir { get; } = Find();

    public static string Read(string name) => File.ReadAllText(Path.Combine(Dir, name));

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures", "simbrief");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests/fixtures/simbrief not found");
    }
}

public class OfpParserTests
{
    [Fact]
    public void V2Sample_MapsEveryField()
    {
        var plan = OfpParser.Parse(Samples.Read("sample-v2.json"));

        Assert.Equal("IBE1234", plan.Callsign);
        Assert.Null(plan.Telephony); // the renderer looks it up from the airline table
        Assert.Equal("A320", plan.AircraftType);
        Assert.Equal(("LEMD", "LEBL", "LERS"), (plan.Origin, plan.Destination, plan.Alternate));
        Assert.Equal(AltitudeTarget.FlightLevel(350), plan.CruiseLevel);
        Assert.Equal(("TESTA1A", "TESTB1B"), (plan.PlannedSid, plan.PlannedStar));
        Assert.Equal(("36L", "24R"), (plan.DepartureRunway, plan.ArrivalRunway));

        Assert.Equal(["TS001", "TESTA", "OSKYA", "OSKYB", "TS002"], plan.Route.Select(w => w.Ident)); // the destination airport is not a route point
        Assert.Equal([null, null, "UN100", null, null], plan.Route.Select(w => w.Airway));            // no airway on SID/STAR or direct legs
        Assert.Equal(new GeoPoint(41.0, -2.0), plan.Route[2].Position);
    }

    [Fact]
    public void V1Shape_AndMissingFields_AreHandled()
    {
        var plan = OfpParser.Parse(Samples.Read("sample-v1.json"));

        Assert.Equal("ECTST", plan.Callsign);                 // no ATC callsign: airline + flight number
        Assert.Equal(("LELC", "LEAL", "LEMI"), (plan.Origin, plan.Destination, plan.Alternate));
        Assert.Equal(AltitudeTarget.Feet(9000), plan.CruiseLevel); // below the 13,000 ft transition altitude
        Assert.Null(plan.PlannedSid);
        Assert.Null(plan.ArrivalRunway);
        Assert.Equal(["TS100"], plan.Route.Select(w => w.Ident)); // the point without a latitude is left out
    }

    [Theory]
    [InlineData("error-unknown-user.json", FlightPlanImportError.UnknownPilot)]
    [InlineData(null, FlightPlanImportError.BadResponse)]
    public void Errors(string? file, FlightPlanImportError expected)
    {
        string json = file is null ? "<html>maintenance</html>" : Samples.Read(file);
        Assert.Equal(expected, Assert.Throws<FlightPlanImportException>(() => OfpParser.Parse(json)).Error);
    }

    [Fact]
    public void APlanWithoutAirports_IsNoFlightPlan()
    {
        var e = Assert.Throws<FlightPlanImportException>(() => OfpParser.Parse("""{"fetch":{"status":"Success"},"general":{}}"""));
        Assert.Equal(FlightPlanImportError.NoFlightPlan, e.Error);
        Assert.Contains("Generate a new flight plan", e.Message);
    }

    /// <summary>The owner's real OFP, kept local (Navigraph's output); skipped elsewhere.</summary>
    [Fact]
    public void OwnersRealOfp_WhenPresent()
    {
        string path = Path.Combine(Samples.Dir, "owner-latest.ofp.json");
        if (!File.Exists(path)) return;
        var plan = OfpParser.Parse(File.ReadAllText(path));
        Assert.Matches("^[A-Z]{4}$", plan.Origin);
        Assert.Matches("^[A-Z]{4}$", plan.Destination);
        Assert.NotEmpty(plan.Callsign);
        Assert.True(plan.Route.Count > 10);
        Assert.NotNull(plan.CruiseLevel);
    }
}

public class SimBriefClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static (SimBriefClient Client, FakeHandler Handler) Client(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return (new SimBriefClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task FetchesTheLatestPlan_ByPilotId_AsJsonV2()
    {
        var (client, handler) = Client(HttpStatusCode.OK, Samples.Read("sample-v2.json"));
        var plan = await client.FetchLatestAsync(" 1234567 ");
        Assert.Equal("IBE1234", plan.Callsign);
        Assert.Equal("https://www.simbrief.com/api/xml.fetcher.php?userid=1234567&json=v2", Assert.Single(handler.Requests).ToString());
        Assert.NotNull(client.LastResponse);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12345678")]
    public async Task MissingOrMalformedPilotId_IsRejectedWithoutARequest(string? id)
    {
        var (client, handler) = Client(HttpStatusCode.OK, "{}");
        var e = await Assert.ThrowsAsync<FlightPlanImportException>(() => client.FetchLatestAsync(id));
        Assert.Equal(FlightPlanImportError.MissingPilotId, e.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnknownPilot_FromHttp400_JsonOrXml()
    {
        var (json, _) = Client(HttpStatusCode.BadRequest, Samples.Read("error-unknown-user.json"));
        Assert.Equal(FlightPlanImportError.UnknownPilot, (await Assert.ThrowsAsync<FlightPlanImportException>(() => json.FetchLatestAsync("1"))).Error);

        var (xml, _) = Client(HttpStatusCode.BadRequest, "<OFP><fetch><status>Error: Unknown UserID</status></fetch></OFP>");
        Assert.Equal(FlightPlanImportError.UnknownPilot, (await Assert.ThrowsAsync<FlightPlanImportException>(() => xml.FetchLatestAsync("1"))).Error);
    }

    [Fact]
    public async Task NoPlan_FromAnotherFetchError()
    {
        var (client, _) = Client(HttpStatusCode.BadRequest, """{"fetch":{"status":"Error: No flight plan on file"}}""");
        Assert.Equal(FlightPlanImportError.NoFlightPlan, (await Assert.ThrowsAsync<FlightPlanImportException>(() => client.FetchLatestAsync("1"))).Error);
    }

    [Fact]
    public async Task NetworkFailures_AreNetworkErrors()
    {
        var down = new SimBriefClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host"))));
        var e = await Assert.ThrowsAsync<FlightPlanImportException>(() => down.FetchLatestAsync("1"));
        Assert.Equal(FlightPlanImportError.Network, e.Error);
        Assert.Contains("internet connection", e.Message);

        var (server, _) = Client(HttpStatusCode.ServiceUnavailable, "");
        Assert.Equal(FlightPlanImportError.Network, (await Assert.ThrowsAsync<FlightPlanImportException>(() => server.FetchLatestAsync("1"))).Error);

        var slow = new SimBriefClient(new HttpClient(new SlowHandler())) { Timeout = TimeSpan.FromMilliseconds(50) };
        Assert.Equal(FlightPlanImportError.Network, (await Assert.ThrowsAsync<FlightPlanImportException>(() => slow.FetchLatestAsync("1"))).Error);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancel);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

public class PlnParserTests
{
    [Fact]
    public void Msfs2024Layout_ProceduresAndRunwaysFromTheDetails_PointsByIdentAndRegion()
    {
        var pln = PlnParser.Parse(Samples.Read("sample-2024.pln"));

        Assert.Equal(("LEMD", "LEBL", 35000), (pln.Origin, pln.Destination, pln.CruiseFt));
        Assert.Equal(("36L", "02R"), (pln.DepartureRunway, pln.ArrivalRunway));
        Assert.Equal(("TESTA1A", "TESTB1B"), (pln.Sid, pln.Star));
        Assert.Equal([("TESTA", "LE", 'W'), ("OSK", "LE", 'V'), ("TB", "LE", 'N')], pln.Waypoints.Select(w => (w.Ident, w.Region, w.Kind)));
        Assert.All(pln.Waypoints, w => Assert.Null(w.Position));
        Assert.Equal("UN100", pln.Waypoints[1].Airway);
    }

    [Fact]
    public void ToFlightPlan_ResolvesPositions_AndDropsUnknownPoints()
    {
        var pln = PlnParser.Parse(Samples.Read("sample-2024.pln"));
        var plan = pln.ToFlightPlan("IBE1234", "Iberia", "A320",
            w => w.Ident == "TB" ? null : new GeoPoint(40, w.Ident.Length), transitionAltitudeFt: 13_000);

        Assert.Equal(("IBE1234", "Iberia", "A320"), (plan.Callsign, plan.Telephony, plan.AircraftType));
        Assert.Equal(["TESTA", "OSK"], plan.Route.Select(w => w.Ident));
        Assert.Equal(AltitudeTarget.FlightLevel(350), plan.CruiseLevel);
        Assert.Equal(("36L", "02R", "TESTA1A", "TESTB1B"), (plan.DepartureRunway, plan.ArrivalRunway, plan.PlannedSid, plan.PlannedStar));
    }

    [Fact]
    public void OlderLayout_WorldPositions_AndProceduresOnTheWaypoints()
    {
        var pln = PlnParser.Parse(Samples.Read("sample-2020.pln"));

        Assert.Equal(("LEMD", "LEVD", 9000), (pln.Origin, pln.Destination, pln.CruiseFt));
        Assert.Equal(("14R", "23", "TESTC2C"), (pln.DepartureRunway, pln.ArrivalRunway, pln.Sid));
        Assert.Null(pln.Star);
        Assert.Equal(["TESTC", "USR01"], pln.Waypoints.Select(w => w.Ident));
        Assert.Equal('U', pln.Waypoints[1].Kind);
        var p = pln.Waypoints[0].Position!.Value;
        Assert.Equal(41.008333, p.Latitude, 5);
        Assert.Equal(-4.25, p.Longitude, 5);
        Assert.Equal(new GeoPoint(-1.5, 10.01), pln.Waypoints[1].Position);

        var plan = pln.ToFlightPlan("ECTST", null, "C172", transitionAltitudeFt: 13_000);
        Assert.Equal(AltitudeTarget.Feet(9000), plan.CruiseLevel);
        Assert.Equal(2, plan.Route.Count); // positions in the file: no lookup needed
    }

    [Theory]
    [InlineData("not xml", FlightPlanImportError.BadResponse)]
    [InlineData("<SimBase.Document/>", FlightPlanImportError.BadResponse)]
    [InlineData("<SimBase.Document><FlightPlan.FlightPlan><DepartureID>LEMD</DepartureID></FlightPlan.FlightPlan></SimBase.Document>", FlightPlanImportError.NoFlightPlan)]
    public void BadFiles(string xml, FlightPlanImportError expected) =>
        Assert.Equal(expected, Assert.Throws<FlightPlanImportException>(() => PlnParser.Parse(xml)).Error);

    /// <summary>The owner's SimBrief MSFS 2024 export, kept local; skipped elsewhere.</summary>
    [Fact]
    public void OwnersRealPln_WhenPresent()
    {
        foreach (var path in Directory.GetFiles(Samples.Dir, "*.local.pln"))
        {
            var pln = PlnParser.Parse(File.ReadAllText(path));
            Assert.Matches("^[A-Z]{4}$", pln.Origin);
            Assert.Matches("^[A-Z]{4}$", pln.Destination);
            Assert.True(pln.Waypoints.Count > 10, path);
            Assert.NotNull(pln.DepartureRunway);
            Assert.NotNull(pln.Sid);
        }
    }
}
