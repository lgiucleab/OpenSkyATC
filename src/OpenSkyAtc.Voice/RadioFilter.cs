namespace OpenSkyAtc.Voice;

public sealed record RadioFilterOptions
{
    /// <summary>Design doc: band-pass about 300-3,000 Hz.</summary>
    public double LowCutHz { get; init; } = 300;
    public double HighCutHz { get; init; } = 3000;

    /// <summary>Background hiss, RMS relative to full scale (design doc: "slight noise"); 0 for none.</summary>
    public double NoiseLevel { get; init; } = 0.012;

    /// <summary>Soft-clip drive: 1 is clean; a little more gives the compressed sound of AM radio.</summary>
    public double Drive { get; init; } = 1.8;

    /// <summary>Squelch: a short burst of noise when the carrier opens and closes.</summary>
    public TimeSpan SquelchOpen { get; init; } = TimeSpan.FromMilliseconds(70);
    public TimeSpan SquelchTail { get; init; } = TimeSpan.FromMilliseconds(140);
    public double SquelchLevel { get; init; } = 0.06;
}

/// <summary>
/// Makes clean TTS sound like a VHF radio transmission (design doc "Voices": in the audio layer, not the brain):
/// band-pass, normalise and soft-clip, then band-limited hiss and squelch bursts. Deterministic for a seed.
/// </summary>
public static class RadioFilter
{
    public static AudioClip Apply(AudioClip clean, int seed, RadioFilterOptions? options = null)
    {
        var o = options ?? new RadioFilterOptions();
        int rate = clean.SampleRate;
        int open = (int)(o.SquelchOpen.TotalSeconds * rate), tail = (int)(o.SquelchTail.TotalSeconds * rate);
        var output = new float[open + clean.Samples.Length + tail];

        // Voice: band-pass, normalise to 0.7 peak, soft-clip.
        var voice = BandPass(clean.Samples, rate, o.LowCutHz, o.HighCutHz);
        float peak = voice.Length == 0 ? 0 : voice.Max(MathF.Abs);
        float gain = peak > 1e-6f ? 0.7f / peak : 0;
        double norm = Math.Tanh(o.Drive * 0.7);
        for (int i = 0; i < voice.Length; i++)
            output[open + i] = (float)(Math.Tanh(o.Drive * voice[i] * gain) / norm * 0.7);

        // Hiss under the whole transmission, louder during the squelch bursts; band-limited like the voice.
        var rng = new System.Random(seed);
        var noise = new float[output.Length];
        for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 2 - 1);
        noise = BandPass(noise, rate, o.LowCutHz, o.HighCutHz);
        double rms = Math.Sqrt(noise.Average(x => (double)x * x));
        if (rms > 0)
        {
            for (int i = 0; i < output.Length; i++)
            {
                bool squelch = i < open || i >= open + clean.Samples.Length;
                double level = squelch ? o.SquelchLevel : o.NoiseLevel;
                output[i] = Math.Clamp(output[i] + (float)(noise[i] / rms * level), -1f, 1f);
            }
        }
        return new AudioClip(output, rate);
    }

    /// <summary>Fourth-order band-pass: two second-order high-pass and two low-pass sections (RBJ cookbook biquads).</summary>
    internal static float[] BandPass(float[] input, int rate, double lowHz, double highHz)
    {
        var x = input;
        foreach (var section in new[] { Biquad.HighPass(rate, lowHz), Biquad.HighPass(rate, lowHz), Biquad.LowPass(rate, highHz), Biquad.LowPass(rate, highHz) })
            x = section.Process(x);
        return x;
    }

    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private const double Q = 0.7071; // Butterworth

        public static Biquad LowPass(int rate, double hz)
        {
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * Q), cos = Math.Cos(w), a0 = 1 + alpha;
            return new((1 - cos) / 2 / a0, (1 - cos) / a0, (1 - cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        public static Biquad HighPass(int rate, double hz)
        {
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * Q), cos = Math.Cos(w), a0 = 1 + alpha;
            return new((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        public float[] Process(float[] input)
        {
            var output = new float[input.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < input.Length; i++)
            {
                double x0 = input[i];
                double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                output[i] = (float)y0;
                x2 = x1; x1 = x0; y2 = y1; y1 = y0;
            }
            return output;
        }
    }
}
