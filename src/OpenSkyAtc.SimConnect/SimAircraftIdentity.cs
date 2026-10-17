using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using SC = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// The user aircraft's ATC identity as set in the sim's flight setup: airline (its radio name, "Iberia"), flight
/// number and tail number. A .pln flight plan has no callsign, so it comes from here (docs/decisions.md, step 11).
/// </summary>
public sealed record SimAircraftIdentity(string? Title, string? Airline, string? FlightNumber, string? TailNumber)
{
    private enum Definitions { Identity }
    private enum Requests { Identity }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RawIdentity
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Title;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Airline;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] FlightNumber;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] TailNumber;
    }

    /// <summary>Reads it on a short connection of its own. Null when the sim is not running or does not answer.</summary>
    public static SimAircraftIdentity? Read(TimeSpan timeout)
    {
        using var signal = new AutoResetEvent(false);
        SC sim;
        try
        {
            sim = new SC("OpenSkyATC aircraft identity", IntPtr.Zero, 0, signal, 0);
        }
        catch (COMException)
        {
            return null;
        }

        using (sim)
        {
            SimAircraftIdentity? result = null;
            sim.OnRecvSimobjectData += (_, d) =>
            {
                var raw = (RawIdentity)d.dwData[0];
                result = new SimAircraftIdentity(Text(raw.Title), Text(raw.Airline), Text(raw.FlightNumber), Text(raw.TailNumber));
            };
            sim.AddToDataDefinition(Definitions.Identity, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SC.SIMCONNECT_UNUSED);
            sim.AddToDataDefinition(Definitions.Identity, "ATC AIRLINE", null, SIMCONNECT_DATATYPE.STRING64, 0, SC.SIMCONNECT_UNUSED);
            sim.AddToDataDefinition(Definitions.Identity, "ATC FLIGHT NUMBER", null, SIMCONNECT_DATATYPE.STRING64, 0, SC.SIMCONNECT_UNUSED);
            sim.AddToDataDefinition(Definitions.Identity, "ATC ID", null, SIMCONNECT_DATATYPE.STRING64, 0, SC.SIMCONNECT_UNUSED);
            sim.RegisterDataDefineStruct<RawIdentity>(Definitions.Identity);
            sim.RequestDataOnSimObject(Requests.Identity, Definitions.Identity, SC.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.ONCE, 0, 0, 0, 0);

            var deadline = DateTime.UtcNow + timeout;
            while (result is null && DateTime.UtcNow < deadline)
            {
                signal.WaitOne(TimeSpan.FromMilliseconds(100));
                try
                {
                    sim.ReceiveMessage();
                }
                catch (COMException)
                {
                    return null;
                }
            }
            return result;
        }
    }

    private static string? Text(byte[]? bytes)
    {
        if (bytes is null) return null;
        int end = Array.IndexOf(bytes, (byte)0);
        string s = System.Text.Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end).Trim();
        return s.Length == 0 ? null : s;
    }
}
