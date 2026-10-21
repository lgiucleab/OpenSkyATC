using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Host;
using OpenSkyAtc.Phraseology;
using OpenSkyAtc.Replay.Tests;
using OpenSkyAtc.Replay.Tests.Harness;
using static OpenSkyAtc.Replay.Tests.Harness.SyntheticDeparture;

namespace OpenSkyAtc.Host.Tests;

public class SettingsStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"openskyatc-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Settings_RoundTrip()
    {
        var store = new SettingsStore(TempFile());
        var settings = new AppSettings
        {
            PlanSource = PlanSource.PlnFile, SimBriefPilotId = "1234567", LastPlnPath = @"C:\plans\a.pln", Volume = 0.5f,
            VoiceModels = ["vctk"], Language = "es", HardMode = true, ArrowSpacingM = 15, FirstRunDone = true, MainWindow = new WindowPlacement(10, 20, 800, 600),
        };
        store.Save(settings);
        var back = store.Load();
        Assert.Equal(settings with { VoiceModels = back.VoiceModels }, back);
        Assert.Equal(["vctk"], back.VoiceModels);
        File.Delete(store.Path);
    }

    [Fact]
    public void MissingPartialOrBrokenFiles_GiveDefaults()
    {
        var store = new SettingsStore(TempFile());
        Assert.Equal(300, store.Load().ArrowLookAheadM);

        File.WriteAllText(store.Path, """{ "language": "es" }""");
        var partial = store.Load();
        Assert.Equal("es", partial.Language);
        Assert.True(partial.TaxiArrows);
        Assert.False(partial.FirstRunDone); // an older file: the guide shows only if no plan source is set either
        Assert.Equal(6, partial.VoiceModels.Count);

        File.WriteAllText(store.Path, "{ not json");
        Assert.Equal("en", store.Load().Language);
        Assert.True(File.Exists(store.Path + ".bad"));
        File.Delete(store.Path);
        File.Delete(store.Path + ".bad");
    }

    [Fact]
    public void DataBundle_LoadsTheShippedFiles() => Assert.True(DataBundle.Load().Callsigns.Count > 1000);
}

public class TranscriptTests
{
    [Fact]
    public void AFlight_BecomesReadableLines()
    {
        var brain = new AtcBrain();
        ReplayRunner.Run(brain, Build(), IntentScript.Parse(DepartureScenarioTests.NormalRequests), Plan, Origin, Destination, seed: 1);
        var lines = new TranscriptWriter(brain.Session, new IcaoRenderer(1), "Vueling 1234").TakeNew();

        var first = lines.First(l => l.Kind == TranscriptKind.Pilot);
        Assert.Equal("Request IFR clearance, Vueling 1234", first.Text);
        Assert.Equal(DeliveryKhz, first.FrequencyKhz);
        var clearance = lines.First(l => l.Kind == TranscriptKind.Atc);
        Assert.Equal("Testville Delivery", clearance.Station);
        Assert.Contains("cleared to XDST", clearance.Text);
        Assert.Contains(lines, l => l.Kind == TranscriptKind.Pilot && l.Text.StartsWith("Cleared to XDST, ") && l.Text.Contains("squawk ") && l.Text.EndsWith(", Vueling 1234"));
        Assert.Contains(lines, l => l is { Kind: TranscriptKind.Event, Text: "Phase: cleared" });
        Assert.True(lines.Zip(lines.Skip(1)).All(p => p.First.Time <= p.Second.Time));
    }

    [Fact]
    public void TakeNew_OnlyReturnsWhatIsNew()
    {
        var brain = new AtcBrain();
        ReplayRunner.Run(brain, Build(), IntentScript.Parse("00:10 RequestClearance"), Plan, Origin, Destination, seed: 1);
        var writer = new TranscriptWriter(brain.Session, new IcaoRenderer(1));
        Assert.NotEmpty(writer.TakeNew());
        Assert.Empty(writer.TakeNew());
    }

    [Theory]
    [InlineData(ReadbackItem.Frequency, "121630", "121.630")]
    [InlineData(ReadbackItem.InitialClimb, "5000FT", "5000 ft")]
    [InlineData(ReadbackItem.Altitude, "FL120", "FL120")]
    [InlineData(ReadbackItem.Clearance, "LINEUP", "lining up")]
    [InlineData(ReadbackItem.HoldShorts, "06,24L", "hold short 06, 24L")]
    [InlineData(ReadbackItem.Route, "FLIGHT PLANNED ROUTE", "flight planned route")]
    public void ReadbackValues_AreSaidLikeAPilot(ReadbackItem item, string value, string said) =>
        Assert.Equal(said, PilotPhrase.Value(item, value));

    [Fact]
    public void PilotCalls_HaveShortPhrases()
    {
        Assert.Equal("Request push and start", PilotPhrase.Of(new RequestPushStart()));
        Assert.Equal("Checking in, passing 4,500 ft for FL120", PilotPhrase.Of(new CheckIn(4500, AltitudeTarget.FlightLevel(120))));
        Assert.Equal("Roger", PilotPhrase.Of(new Readback(new Dictionary<ReadbackItem, string>())));
    }
}

public class AutoTuneTests
{
    private static readonly OpenSkyAtc.Core.Model.ControllerUnit Ground = new(OpenSkyAtc.Core.Model.ControllerRole.Ground, "Barajas Ground", 121_630, OtherFrequenciesKhz: [121_850]);

    private static OpenSkyAtc.Core.Sim.SimState Radios(int com1, int com2, bool txOn2 = false) => new()
    {
        Com1ActiveKhz = com1, Com2ActiveKhz = com2, Com1Transmit = !txOn2, Com2Transmit = txOn2,
    };

    [Fact]
    public void TunesTheRadioThePilotTransmitsOn() =>
        Assert.Equal((1, 121_630), AutoTune.Plan(Radios(121_800, 118_250), Ground));

    [Fact]
    public void TransmittingOnCom2_TunesCom2() =>
        Assert.Equal((2, 121_630), AutoTune.Plan(Radios(118_250, 121_800, txOn2: true), Ground));

    [Fact]
    public void AlreadyOnTheUnit_NothingToDo()
    {
        Assert.Null(AutoTune.Plan(Radios(121_630, 118_250), Ground));
        Assert.Null(AutoTune.Plan(Radios(121_850, 118_250), Ground)); // the unit's other frequency counts
    }
}

public class VoiceChoiceTests
{
    [Fact]
    public void NaturalVoices_AreKokoro_FastVoicesAreTheChosenPiperOnes()
    {
        Assert.Equal([OpenSkyAtc.Voice.VoiceCatalog.Kokoro], FlightHost.Voices(new AppSettings()));
        var fast = FlightHost.Voices(new AppSettings { NaturalVoices = false, VoiceModels = ["vctk", "kokoro"] });
        Assert.Equal(["vctk"], fast.Select(m => m.Id));
        Assert.Equal(6, FlightHost.Voices(new AppSettings { NaturalVoices = false, VoiceModels = [] }).Count);
    }
}
