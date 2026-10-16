using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Messages;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Radio;

namespace OpenSkyAtc.Phraseology.Tests;

/// <summary>One sample of every message part, used by the snapshot and coverage tests.</summary>
internal static class Samples
{
    public static readonly ControllerUnit Delivery = new(ControllerRole.Delivery, "Barcelona Delivery", 121_850);
    public static readonly ControllerUnit Ground = new(ControllerRole.Ground, "Barcelona Ground", 121_700);
    public static readonly ControllerUnit Tower = new(ControllerRole.Tower, "Barcelona Tower", 118_100);
    public static readonly ControllerUnit Control = new(ControllerRole.Center, "Madrid Control", 132_350);

    public static readonly (string Name, AtcMessagePart Part)[] Parts =
    [
        ("IfrClearance SID", new IfrClearance("LEMD", "Madrid", "NAPSI1C", AltitudeTarget.Feet(5000), 4521)),
        ("IfrClearance no SID, no name", new IfrClearance("LEMD", null, null, AltitudeTarget.FlightLevel(70), 443)),
        ("ClearedTo", new ClearedTo("LEMD", "Madrid")),
        ("Via SID", new Via("NAPSI1C")),
        ("Via route", new Via(null)),
        ("ClimbTo FL", new ClimbTo(AltitudeTarget.FlightLevel(120))),
        ("ClimbTo altitude", new ClimbTo(AltitudeTarget.Feet(3500))),
        ("DescendTo FL", new DescendTo(AltitudeTarget.FlightLevel(80))),
        ("DescendTo altitude", new DescendTo(AltitudeTarget.Feet(2000))),
        ("TurnHeading left", new TurnHeading(TurnDirection.Left, 270)),
        ("TurnHeading right", new TurnHeading(TurnDirection.Right, 90)),
        ("TurnHeading fly", new TurnHeading(TurnDirection.None, 5)),
        ("Direct", new Direct("NAPSI")),
        ("Speed maintain", new Speed(250)),
        ("Speed reduce", new Speed(180, SpeedAction.Reduce)),
        ("Speed increase", new Speed(220, SpeedAction.Increase)),
        ("Qnh", new Qnh(1013)),
        ("Squawk", new Squawk(4521)),
        ("ReadbackCorrect", new ReadbackCorrect()),
        ("Contact", new Contact(Tower)),
        ("Contact area control", new Contact(Control)),
        ("Contact when ready", new Contact(Ground, ContactCondition.WhenReadyForPushAndStart)),
        ("PushStartApproved", new PushStartApproved()),
        ("TaxiVia fallback", new TaxiVia(null, "24L", [], [])),
        ("TaxiVia route", new TaxiVia("A1", "24L", ["K", "M"], ["02"])),
        ("TaxiHoldingPoint", new TaxiHoldingPoint("G2")),
        ("RunwayIs", new RunwayIs("06R")),
        ("HoldShort", new HoldShort("02")),
        ("LineUpAndWait", new LineUpAndWait("24L")),
        ("Wind", new Wind(250, 12)),
        ("Wind calm", new Wind(0, 0)),
        ("ClearedTakeoff", new ClearedTakeoff("24L")),
        ("ClearedLand", new ClearedLand("24R")),
        ("Negative", new Negative()),
        ("Negative repeated", new Negative(Repeated: true)),
        ("ReadBackRequest", new ReadBackRequest([ReadbackItem.Squawk, ReadbackItem.InitialClimb])),
        ("StandBy", new StandBy()),
        ("SayAgainRequest", new SayAgainRequest()),
        ("UnableContact", new UnableContact(Delivery)),
        ("Roger", new Roger()),
        ("ClimbTo immediately", new ClimbTo(AltitudeTarget.FlightLevel(120), Immediately: true)),
        ("DescendTo immediately", new DescendTo(AltitudeTarget.Feet(5000), Immediately: true)),
        ("CheckAltitude", new CheckAltitude(AltitudeTarget.FlightLevel(120))),
        ("ConfirmVertical climbing", new ConfirmVertical(true, AltitudeTarget.FlightLevel(240))),
        ("ConfirmVertical descending", new ConfirmVertical(false, AltitudeTarget.Feet(3000))),
        ("TurnHeading immediately", new TurnHeading(TurnDirection.Left, 270, Immediately: true)),
        ("CheckHeading", new CheckHeading()),
        ("SayIntentions", new SayIntentions()),
        ("Squawk recycle", new Squawk(4521, Recycle: true)),
        ("HoldPosition", new HoldPosition()),
        ("StopImmediately", new StopImmediately()),
        ("StopImmediately emphasis", new StopImmediately(Emphasis: true)),
        ("GoAround", new GoAround()),
        ("GoAround repeated", new GoAround(Repeated: true)),
        ("FlyRunwayHeading", new FlyRunwayHeading()),
        ("RadarContact", new RadarContact()),
        ("Identified", new Identified()),
        ("ConfirmAltitude", new ConfirmAltitude()),
        ("ExpectApproach", new ExpectApproach("ILS", "24R")),
        ("ClearedApproach", new ClearedApproach("ILS", "24R")),
        ("ReportEstablishedRequest", new ReportEstablishedRequest()),
        ("VacateWhenAble", new VacateWhenAble()),
        ("TaxiToStand", new TaxiToStand("27")),
        ("TaxiToStand parking", new TaxiToStand(null)),
        ("ClosingCall", new ClosingCall()),
        ("ClearedApproach via STAR", new ClearedApproach("ILS", "24R", "NAPSI1A")),
        ("VectoringFor", new VectoringFor("ILS", "24R")),
        ("ClosingLocaliser right", new ClosingLocaliser(true)),
        ("ClosingLocaliser left", new ClosingLocaliser(false)),
        ("SpeedUntil", new SpeedUntil(160, 4)),
        ("Mayday", new EmergencyAcknowledged(EmergencyKind.Mayday)),
        ("PanPan", new EmergencyAcknowledged(EmergencyKind.PanPan)),
    ];

    public static AtcMessage Message(ControllerUnit unit, string code, string? telephony, params AtcMessagePart[] parts) =>
        new(TimeSpan.Zero, unit, new AircraftCallsign(code, telephony), parts, TransmissionPriority.Reply);
}

public class PartSnapshotTests
{
    private static readonly PhraseologyOptions DocOnly = new() { EuropeanVariants = false, Courtesy = false };

    [Fact]
    public void EveryPartType_HasASample()
    {
        var sampled = Samples.Parts.Select(p => p.Part.GetType()).ToHashSet();
        var all = typeof(AtcMessagePart).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AtcMessagePart)) && !t.IsAbstract);
        Assert.Empty(all.Where(t => !sampled.Contains(t)).Select(t => t.Name));
    }

    /// <summary>
    /// Every part's design-doc wording and every enabled variant, display and speech, in one approved file
    /// (tests/fixtures/snapshots/phraseology-parts.approved.txt). Variants are collected over many seeds.
    /// </summary>
    [Fact]
    public void AllPartsAndVariants_MatchSnapshot()
    {
        var lines = new List<string>();
        foreach (var (name, part) in Samples.Parts)
        {
            var doc = new IcaoRenderer(1, DocOnly).RenderPart(part);
            lines.Add($"## {name}");
            lines.Add($"doc     | {doc.Display} | {doc.Speech}");

            var variants = Enumerable.Range(1, 64)
                .Select(seed => new IcaoRenderer(seed).RenderPart(part))
                .Distinct()
                .Where(v => v != doc)
                .OrderBy(v => v.Display, StringComparer.Ordinal);
            foreach (var v in variants) lines.Add($"variant | {v.Display} | {v.Speech}");
        }
        Snapshot.Verify("phraseology-parts", lines);
    }

    [Fact]
    public void WindWithClearance_BothPlacements()
    {
        var seen = Enumerable.Range(1, 64)
            .Select(seed => new IcaoRenderer(seed, new PhraseologyOptions { StationNameOnFirstContact = false })
                .Render(Samples.Message(Samples.Tower, "VLG1234", "Vueling", new Wind(250, 12), new ClearedTakeoff("24L"))))
            .Distinct()
            .ToList();

        Assert.Equal(2, seen.Count);
        Assert.Contains(new RenderedMessage(
            "Vueling 1234, wind 250/12, runway 24L, cleared for takeoff",
            "Vueling one two three four, wind two five zero degrees, one two knots, runway two four left, cleared for takeoff"), seen);
        Assert.Contains(new RenderedMessage(
            "Vueling 1234, runway 24L, cleared for takeoff, surface wind 250/12",
            "Vueling one two three four, runway two four left, cleared for takeoff, surface wind two five zero degrees, one two knots"), seen);
    }

    [Fact]
    public void DocOnly_GivesOneWordingPerPart()
    {
        foreach (var (name, part) in Samples.Parts)
        {
            var distinct = Enumerable.Range(1, 32).Select(s => new IcaoRenderer(s, DocOnly).RenderPart(part)).Distinct().Count();
            Assert.True(distinct == 1, $"{name} has {distinct} wordings with European variants off");
        }
    }
}

public class MessageRenderingTests
{
    private static readonly PhraseologyOptions Plain = new() { EuropeanVariants = false, Courtesy = false };

    [Fact]
    public void FirstTransmission_IncludesTheStationName_LaterOnesDoNot()
    {
        var r = new IcaoRenderer(1, Plain with { StationNameOnFirstContact = true });
        var first = r.Render(Samples.Message(Samples.Delivery, "VLG1234", "Vueling", new Squawk(4521)));
        var second = r.Render(Samples.Message(Samples.Delivery, "VLG1234", "Vueling", new Squawk(4521)));

        Assert.Equal("Vueling 1234, Barcelona Delivery, squawk 4521", first.Display);
        Assert.Equal("Vueling one two three four, Barcelona Delivery, squawk four five two one", first.Speech);
        Assert.Equal("Vueling 1234, squawk 4521", second.Display);
    }

    [Fact]
    public void Telephony_ComesFromTheTable_WhenThePlanHasNone()
    {
        var table = CallsignTable.Parse(["# test", "IBE,IBERIA", "VLG,VUELING"]);
        var r = new IcaoRenderer(1, Plain with { StationNameOnFirstContact = false }, table);
        var m = r.Render(Samples.Message(Samples.Tower, "IBE3456", null, new Roger()));
        Assert.Equal(("Iberia 3456, roger", "Iberia three four five six, roger"), (m.Display, m.Speech));
    }

    [Fact]
    public void UnknownAirline_IsSpelled()
    {
        var m = new IcaoRenderer(1, Plain with { StationNameOnFirstContact = false })
            .Render(Samples.Message(Samples.Tower, "XYZ12A", null, new Roger()));
        Assert.Equal(("XYZ12A, roger", "X-ray Yankee Zulu one two Alpha, roger"), (m.Display, m.Speech));
    }

    [Fact]
    public void Registration_IsFullFirst_ThenAbbreviated()
    {
        var r = new IcaoRenderer(1, Plain with { StationNameOnFirstContact = false });
        var first = r.Render(Samples.Message(Samples.Tower, "EC-ABC", null, new Roger()));
        var later = r.Render(Samples.Message(Samples.Tower, "EC-ABC", null, new Roger()));

        Assert.Equal(("EC-ABC, roger", "Echo Charlie Alpha Bravo Charlie, roger"), (first.Display, first.Speech));
        Assert.Equal(("E-BC, roger", "Echo Bravo Charlie, roger"), (later.Display, later.Speech));
    }

    [Fact]
    public void Courtesy_IsAddedToEveryHandoff_WhenOn()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var on = new IcaoRenderer(seed).RenderPart(new Contact(Samples.Tower));
            Assert.True(on.Display.EndsWith(", good day") || on.Display.EndsWith(", bye bye"), on.Display);
        }
        var off = new IcaoRenderer(1, Plain).RenderPart(new Contact(Samples.Tower));
        Assert.Equal("contact Barcelona Tower 118.100", off.Display);
        Assert.Equal("contact Barcelona Tower one one eight decimal one", off.Speech);
    }

    [Fact]
    public void Destination_IsSpokenByName_AndShownAsIcao()
    {
        var m = new IcaoRenderer(1, Plain).RenderPart(new ClearedTo("LEMD", "Madrid"));
        Assert.Equal(("cleared to LEMD", "cleared to Madrid"), (m.Display, m.Speech));
    }

    [Fact]
    public void SameSeed_GivesTheSameWording()
    {
        var a = new IcaoRenderer(9);
        var b = new IcaoRenderer(9);
        foreach (var (_, part) in Samples.Parts) Assert.Equal(a.RenderPart(part), b.RenderPart(part));
    }

    [Fact]
    public void ShippedCallsignTable_ParsesAndHasTheDesignDocExamples()
    {
        var table = CallsignTable.Load(Snapshot.RepoFile("data", "airline-callsigns.csv"));
        Assert.True(table.Count > 1000, $"only {table.Count} entries");
        Assert.Equal("Iberia", table.Telephony("IBE"));
        Assert.Equal("Vueling", table.Telephony("VLG"));
        Assert.Equal("Ryanair", table.Telephony("RYR"));
        Assert.Equal("KLM", table.Telephony("KLM"));
    }

    [Fact]
    public void CallsignTable_RejectsBadLines()
    {
        Assert.Throws<FormatException>(() => CallsignTable.Parse(["IBERIA"]));
        Assert.Throws<FormatException>(() => CallsignTable.Parse(["IB,IBERIA"]));
    }
}
