using OpenSkyAtc.Core.Radio;
using OpenSkyAtc.Voice;

namespace OpenSkyAtc.Voice.Tests;

/// <summary>The design doc's transmission queue rules, on the playback side.</summary>
public class RadioChannelTests
{
    private const int Ground = 121_700, Tower = 118_100;
    private static readonly Func<int, bool> OnBoth = _ => true;

    private static RadioTransmission T(string text, double seconds, TransmissionPriority priority = TransmissionPriority.Reply, int khz = Ground) =>
        new(khz == Ground ? "Ground" : "Tower", khz, priority, new AudioClip(new float[(int)(seconds * 1000)], 1000), text);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static string? Started(IReadOnlyList<RadioAction> actions) => actions.OfType<StartPlaying>().SingleOrDefault()?.Transmission.Text;

    [Fact]
    public void PlaysOneAtATime_WithAOneSecondGap()
    {
        var ch = new RadioChannel();
        ch.Enqueue(T("first", 3));
        ch.Enqueue(T("second", 2));

        Assert.Equal("first", Started(ch.Update(S(0), OnBoth)));
        Assert.Null(Started(ch.Update(S(2.9), OnBoth)));  // still playing
        Assert.Null(Started(ch.Update(S(3.5), OnBoth)));  // ended at 3 s: gap until 4 s
        Assert.Equal("second", Started(ch.Update(S(4.0), OnBoth)));
    }

    [Fact]
    public void NeverStartsWhileThePilotTransmits_AndWaitsTheGapAfterwards()
    {
        var ch = new RadioChannel();
        ch.PushToTalk(true, S(0));
        ch.Enqueue(T("reply", 2));
        Assert.Null(Started(ch.Update(S(5), OnBoth)));
        ch.PushToTalk(false, S(6));
        Assert.Null(Started(ch.Update(S(6.5), OnBoth)));
        Assert.Equal("reply", Started(ch.Update(S(7), OnBoth)));
    }

    [Fact]
    public void Urgent_CutsTheCurrentTransmission_AndSkipsTheGap()
    {
        var ch = new RadioChannel();
        ch.Enqueue(T("long info", 10, TransmissionPriority.Info));
        Assert.Equal("long info", Started(ch.Update(S(0), OnBoth)));

        ch.Enqueue(T("go around", 2, TransmissionPriority.Urgent, Tower));
        var actions = ch.Update(S(1), OnBoth);
        Assert.Equal("long info", Assert.Single(actions.OfType<CutOff>()).Transmission.Text);
        Assert.Equal("go around", Started(actions));

        // A second urgent call does not cut the first one.
        ch.Enqueue(T("stop", 1, TransmissionPriority.Urgent));
        Assert.Empty(ch.Update(S(1.5), OnBoth));
        Assert.Equal("stop", Started(ch.Update(S(3), OnBoth))); // no gap for urgent
    }

    [Fact]
    public void Urgent_StillWaitsForThePilotToStopTransmitting()
    {
        var ch = new RadioChannel();
        ch.PushToTalk(true, S(0));
        ch.Enqueue(T("go around", 2, TransmissionPriority.Urgent));
        Assert.Empty(ch.Update(S(1), OnBoth));
        ch.PushToTalk(false, S(2));
        Assert.Equal("go around", Started(ch.Update(S(2), OnBoth)));
    }

    [Fact]
    public void WaitingTransmissions_GoByPriority_ThenArrival()
    {
        var ch = new RadioChannel();
        ch.Enqueue(T("playing", 1));
        ch.Update(S(0), OnBoth);
        ch.Enqueue(T("info", 1, TransmissionPriority.Info));
        ch.Enqueue(T("instruction 1", 1, TransmissionPriority.Instruction));
        ch.Enqueue(T("reply", 1, TransmissionPriority.Reply));
        ch.Enqueue(T("instruction 2", 1, TransmissionPriority.Instruction));

        var order = new List<string?>();
        for (double t = 2; t < 20 && order.Count < 4; t += 0.5)
            if (Started(ch.Update(S(t), OnBoth)) is { } s) order.Add(s);
        Assert.Equal(["reply", "instruction 1", "instruction 2", "info"], order);
    }

    [Fact]
    public void ATransmissionForAFrequencyTheAircraftLeft_IsDropped()
    {
        var ch = new RadioChannel();
        ch.Enqueue(T("ground call", 2));
        ch.Enqueue(T("tower call", 2, khz: Tower));
        var actions = ch.Update(S(0), khz => khz == Tower);
        Assert.Equal("ground call", Assert.Single(actions.OfType<Dropped>()).Transmission.Text);
        Assert.Equal("tower call", Started(actions)); // the next one plays without waiting
    }

    [Fact]
    public void FinishedPlaying_IsReported_AtTheEnd_WhenCut_AndWhenStoppedEarly()
    {
        var ch = new RadioChannel();
        ch.Enqueue(T("first", 2));
        ch.Update(S(0), OnBoth);
        Assert.Empty(ch.Update(S(1), OnBoth).OfType<FinishedPlaying>());
        Assert.Equal("first", Assert.Single(ch.Update(S(2), OnBoth).OfType<FinishedPlaying>()).Transmission.Text);

        ch.Enqueue(T("second", 5));
        ch.Update(S(3), OnBoth);
        ch.Enqueue(T("stop", 1, TransmissionPriority.Urgent));
        Assert.Equal("second", Assert.Single(ch.Update(S(4), OnBoth).OfType<FinishedPlaying>()).Transmission.Text);

        ch.Stopped(S(4.5));
        Assert.Equal("stop", Assert.Single(ch.Update(S(4.6), OnBoth).OfType<FinishedPlaying>()).Transmission.Text);
    }

    [Fact]
    public void StoppedEarly_StartsTheGapThen()
    {
        var ch = new RadioChannel();
        ch.Enqueue(T("first", 10));
        ch.Enqueue(T("second", 1));
        ch.Update(S(0), OnBoth);
        ch.Stopped(S(2));
        Assert.Null(Started(ch.Update(S(2.5), OnBoth)));
        Assert.Equal("second", Started(ch.Update(S(3), OnBoth)));
    }
}

public class RadioFilterTests
{
    private const int Rate = 22_050;

    private static float[] Sine(double hz, double seconds = 1) =>
        Enumerable.Range(0, (int)(Rate * seconds)).Select(i => (float)Math.Sin(2 * Math.PI * hz * i / Rate)).ToArray();

    private static double Rms(float[] x, int skip) => Math.Sqrt(x.Skip(skip).Average(v => (double)v * v));

    [Theory]
    [InlineData(1000, -1)]   // in the voice band: kept
    [InlineData(100, -12)]   // below 300 Hz: cut
    [InlineData(8000, -12)]  // above 3 kHz: cut
    public void BandPass_KeepsTheVoiceBand(double hz, double expectedDb)
    {
        var input = Sine(hz);
        double db = 20 * Math.Log10(Rms(RadioFilter.BandPass(input, Rate, 300, 3000), Rate / 10) / Rms(input, Rate / 10));
        if (expectedDb < -3) Assert.True(db < expectedDb, $"{hz} Hz: {db:F1} dB");
        else Assert.True(db > expectedDb, $"{hz} Hz: {db:F1} dB");
    }

    [Fact]
    public void Apply_AddsSquelch_StaysInRange_AndIsDeterministic()
    {
        var clean = new AudioClip(Sine(800, 2), Rate);
        var a = RadioFilter.Apply(clean, seed: 7);
        var b = RadioFilter.Apply(clean, seed: 7);
        Assert.Equal(a.Samples, b.Samples);
        Assert.Equal(clean.Samples.Length + (int)(0.07 * Rate) + (int)(0.14 * Rate), a.Samples.Length);
        Assert.All(a.Samples, s => Assert.InRange(s, -1f, 1f));
        Assert.True(Rms(a.Samples[..1000], 0) > 0.02, "squelch burst before the voice");
        Assert.NotEqual(a.Samples, RadioFilter.Apply(clean, seed: 8).Samples);
    }
}

public class VoiceAssignerTests
{
    [Fact]
    public void EachUnitKeepsItsVoice_AndUnitsGetDifferentVoices()
    {
        var assigner = new VoiceAssigner(42, VoiceCatalog.Piper);
        var units = new[] { "Barajas Delivery", "Barajas Ground", "Barajas Tower", "Madrid Departure", "Madrid Control", "Barcelona Approach", "Barcelona Tower", "Barcelona Ground" };
        var voices = units.Select(assigner.VoiceFor).ToList();
        Assert.Equal(units.Length, voices.Distinct().Count());
        Assert.Equal(voices, units.Select(assigner.VoiceFor));
        // Every model is used before any repeats (6 Piper models, 8 units).
        Assert.Equal(VoiceCatalog.Piper.Count, voices.Take(6).Select(v => v.ModelId).Distinct().Count());

        // Natural voices: one model, so the units get different Kokoro speakers.
        var kokoro = new VoiceAssigner(42, [VoiceCatalog.Kokoro]);
        var natural = units.Select(kokoro.VoiceFor).ToList();
        Assert.Equal(units.Length, natural.Distinct().Count());
        Assert.All(natural, v => Assert.Equal("kokoro", v.ModelId));
    }

    [Fact]
    public void SameSeed_SameVoices_AndOnlyInstalledModels()
    {
        var installed = new[] { VoiceCatalog.Get("joe"), VoiceCatalog.Get("cori") };
        var a = new VoiceAssigner(1, installed);
        var b = new VoiceAssigner(1, installed);
        Assert.Equal(a.VoiceFor("Ground"), b.VoiceFor("Ground"));
        Assert.Equal(a.VoiceFor("Tower"), b.VoiceFor("Tower"));
        Assert.All(a.Assigned.Values, v => Assert.Contains(v.ModelId, new[] { "joe", "cori" }));
        Assert.NotEqual(a.VoiceFor("Ground"), a.VoiceFor("Tower"));
        Assert.Throws<ArgumentException>(() => new VoiceAssigner(1, []));
    }

    [Fact]
    public void VoiceRefs_RoundTrip_AndDownloadUrlsPointAtTheRelease()
    {
        Assert.Equal(new VoiceRef("vctk", 12), VoiceRef.Parse("vctk:12"));
        Assert.Equal("vctk:12", new VoiceRef("vctk", 12).ToString());
        Assert.All(VoiceCatalog.All, m => Assert.StartsWith(VoiceModel.ReleaseUrl, m.DownloadUrl.ToString()));
        Assert.Equal(109, VoiceCatalog.Get("vctk").Speakers.Count);
    }
}
