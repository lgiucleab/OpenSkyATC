using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SC = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Sends CommBus events by calling the native SimConnect_CallCommBusEvent directly. The managed
/// SimConnect.CallCommBusEvent cannot be used: it measures a string with <c>text.IndexOf("\0")</c>, which on .NET 5+
/// (ICU) returns 0 instead of -1, so it walks past the end of its buffer and crashes the app (seen in the owner's
/// flight, 2026-10-18); it also leaks a copy of every message. Here the text is sent as UTF-8 with its terminator.
/// </summary>
internal static class CommBus
{
    public const uint ToJs = 1;

    private static readonly FieldInfo? HandleField = typeof(SC).GetField("hSimConnect", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>False when the wrapper's handle cannot be found (another SDK version): nothing is sent then.</summary>
    public static bool Available => HandleField?.FieldType == typeof(IntPtr);

    /// <summary>The bytes sent for <paramref name="text"/>: UTF-8 plus the terminating zero.</summary>
    public static byte[] Payload(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);
        return bytes;
    }

    /// <summary>Sends <paramref name="text"/> to <paramref name="eventName"/>; throws COMException when SimConnect refuses it.</summary>
    public static void Call(SC sim, string eventName, uint broadcastTo, string text)
    {
        if (!Available || HandleField!.GetValue(sim) is not IntPtr handle || handle == IntPtr.Zero) return;
        byte[] data = Payload(text);
        int hr = SimConnect_CallCommBusEvent(handle, eventName, broadcastTo, (uint)data.Length, data);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int SimConnect_CallCommBusEvent(IntPtr hSimConnect, string eventName, uint broadcastTo, uint bufferSize, byte[] data);
}
