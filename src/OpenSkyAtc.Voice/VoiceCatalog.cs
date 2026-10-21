namespace OpenSkyAtc.Voice;

/// <summary>Speech engine of a voice model: Kokoro (natural, slower) or Piper (VITS, fast).</summary>
public enum VoiceEngine
{
    Piper,
    Kokoro,
}

/// <summary>
/// A voice model (pre-converted for sherpa-onnx) and its licence. Multi-speaker models give one controller
/// voice per speaker. Licences checked against each voice's MODEL_CARD (docs/decisions.md, step 12).
/// </summary>
/// <param name="Id">Short id used in settings and logs ("vctk").</param>
/// <param name="Archive">Release asset name in the sherpa-onnx tts-models release (also its folder name once unpacked).</param>
/// <param name="Speakers">Speaker ids used for controllers (a subset where quality varies).</param>
public sealed record VoiceModel(string Id, string Archive, string Licence, string Credit, IReadOnlyList<int> Speakers,
    VoiceEngine Engine = VoiceEngine.Piper)
{
    public const string ReleaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/";

    public Uri DownloadUrl => new($"{ReleaseUrl}{Archive}.tar.bz2");
}

/// <summary>One controller voice: a model and a speaker in it.</summary>
public sealed record VoiceRef(string ModelId, int Speaker)
{
    public override string ToString() => $"{ModelId}:{Speaker}";

    public static VoiceRef Parse(string text)
    {
        var parts = text.Split(':');
        return new VoiceRef(parts[0], parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0);
    }
}

/// <summary>
/// The voices: Kokoro by default (owner's choice, refactor phase 4: the most natural local voices) and the Piper
/// voices the owner chose in step 12 (two multi-speaker pools and four single voices) as the fast option.
/// </summary>
public static class VoiceCatalog
{
    /// <summary>
    /// Kokoro-82M v1.0 (sherpa-onnx's multi-language package): its 28 English voices, ids 0-19 US and 20-27 UK. 19
    /// (am_santa, a Father Christmas voice) is left out.
    /// </summary>
    public static readonly VoiceModel Kokoro = new("kokoro", "kokoro-multi-lang-v1_0", "Apache-2.0",
        "Kokoro-82M by hexgrad (Apache 2.0), converted for sherpa-onnx by the k2-fsa project.",
        Enumerable.Range(0, 28).Where(i => i != 19).ToArray(), VoiceEngine.Kokoro);

    /// <summary>
    /// LibriTTS-R speakers are audiobook readers of uneven quality: a spread of 48 ids until the owner has listened to
    /// them (<c>voices audition libritts_r</c>) and kept the clear ones.
    /// </summary>
    private static readonly int[] LibrittsSubset = Enumerable.Range(0, 48).Select(i => i * 19).ToArray();

    public static readonly IReadOnlyList<VoiceModel> All =
    [
        new("vctk", "vits-piper-en_GB-vctk-medium", "CC BY 4.0",
            "CSTR VCTK Corpus, Centre for Speech Technology Research, University of Edinburgh (CC BY 4.0); Piper voice by the Piper project.",
            Enumerable.Range(0, 109).ToArray()),
        new("libritts_r", "vits-piper-en_US-libritts_r-medium", "CC BY 4.0",
            "LibriTTS-R by Koizumi et al., from LibriVox recordings (CC BY 4.0); Piper voice by the Piper project.",
            LibrittsSubset),
        new("joe", "vits-piper-en_US-joe-medium", "CC0", "Joe voice dataset, Open Home Foundation (CC0).", [0]),
        new("norman", "vits-piper-en_US-norman-medium", "Public domain", "Norman voice by Bryce Beattie from LibriVox recordings (public domain).", [0]),
        new("john", "vits-piper-en_US-john-medium", "Public domain", "John voice by Bryce Beattie from LibriVox recordings (public domain).", [0]),
        new("cori", "vits-piper-en_GB-cori-high", "Public domain", "Cori voice by Bryce Beattie from LibriVox recordings (public domain).", [0]),
        Kokoro,
    ];

    /// <summary>The fast (Piper) voices.</summary>
    public static IReadOnlyList<VoiceModel> Piper => All.Where(m => m.Engine == VoiceEngine.Piper).ToList();

    public static VoiceModel Get(string id) =>
        All.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown voice model '{id}'.");
}

/// <summary>
/// Gives each controller unit a fixed voice for the whole session (design doc "Voices"), so the pilot recognises the
/// same controller on a frequency. Seeded, so a replay always gets the same voices; two units never share a voice
/// while unused ones remain.
/// </summary>
public sealed class VoiceAssigner
{
    private readonly List<VoiceRef> _pool;
    private readonly Dictionary<string, VoiceRef> _byUnit = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Random _rng;

    /// <param name="models">Models that are installed; voices from others are never assigned.</param>
    public VoiceAssigner(int seed, IEnumerable<VoiceModel> models)
    {
        _rng = new System.Random(seed);
        // Single-speaker voices and pools weigh the same per model, so 109 VCTK speakers do not crowd out the others.
        _pool = models.SelectMany(m => m.Speakers.Select(s => new VoiceRef(m.Id, s))).ToList();
        if (_pool.Count == 0) throw new ArgumentException("No voice models installed.");
    }

    public IReadOnlyDictionary<string, VoiceRef> Assigned => _byUnit;

    /// <summary>The voice of the unit with this callsign ("Barajas Ground"), chosen the first time it speaks.</summary>
    public VoiceRef VoiceFor(string unitCallsign)
    {
        if (_byUnit.TryGetValue(unitCallsign, out var voice)) return voice;
        var used = _byUnit.Values.ToHashSet();
        var models = _pool.Select(v => v.ModelId).Distinct().ToList();
        // Pick a model first (each model equally likely, favouring ones not used yet, then ones with a free speaker),
        // then a speaker in it.
        var freshModels = models.Where(m => !used.Any(u => u.ModelId == m)).ToList();
        var withFreeSpeaker = models.Where(m => _pool.Any(v => v.ModelId == m && !used.Contains(v))).ToList();
        var modelChoices = freshModels.Count > 0 ? freshModels : withFreeSpeaker.Count > 0 ? withFreeSpeaker : models;
        string model = modelChoices[_rng.Next(modelChoices.Count)];
        var speakers = _pool.Where(v => v.ModelId == model && !used.Contains(v)).ToList();
        if (speakers.Count == 0) speakers = _pool.Where(v => v.ModelId == model).ToList();
        voice = speakers[_rng.Next(speakers.Count)];
        _byUnit[unitCallsign] = voice;
        return voice;
    }
}
