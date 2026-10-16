using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Conversation;

/// <summary>
/// Everything the pilot can say. Buttons and speech recognition both produce these, so the brain has
/// one input path. Catalog from the design doc, plus <see cref="RejectTakeoff"/> from the phase table.
/// </summary>
public abstract record PilotIntent;

/// <param name="AtisLetter">Information letter the pilot reports, if any. Not checked until ATIS is built.</param>
public sealed record RequestClearance(string? AtisLetter = null) : PilotIntent;
public sealed record RequestPushStart : PilotIntent;
public sealed record RequestTaxi : PilotIntent;
public sealed record ReadyForDeparture : PilotIntent;
public sealed record RejectTakeoff : PilotIntent;

/// <param name="AltitudeFt">Altitude the pilot reports passing.</param>
/// <param name="ClearedAltitude">Altitude the pilot reports climbing or descending to.</param>
public sealed record CheckIn(int? AltitudeFt = null, AltitudeTarget? ClearedAltitude = null) : PilotIntent;

public sealed record RequestLevel(AltitudeTarget Level) : PilotIntent;
public sealed record RequestDirect(string Fix) : PilotIntent;
public sealed record RequestDescent : PilotIntent;
public sealed record ReportEstablished : PilotIntent;
public sealed record GoingAround : PilotIntent;
public sealed record ReportVacated : PilotIntent;

/// <summary>
/// A readback. <paramref name="Items"/> holds canonical values (see <see cref="ReadbackValues"/>).
/// <paramref name="ExchangeId"/> names the instruction being read back; null means the latest one.
/// </summary>
public sealed record Readback(IReadOnlyDictionary<ReadbackItem, string> Items, int? ExchangeId = null) : PilotIntent;

public sealed record SayAgain : PilotIntent;
public sealed record Wilco : PilotIntent;
public sealed record Unable(string? Reason = null) : PilotIntent;
public sealed record Mayday : PilotIntent;
public sealed record PanPan : PilotIntent;
