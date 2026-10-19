using System.Runtime.InteropServices;

namespace OpenSkyAtc.Host;

/// <summary>
/// A keyboard push-to-talk that works whichever window has focus (step 14): a Windows low-level keyboard hook on its
/// own thread. The keys still reach the focused app (nothing is swallowed), so pick a key the sim does not use.
/// Key names: "F12", "Ctrl+Space", "RCtrl", "CapsLock", "NumPad0", "Shift+T"; see <see cref="Parse"/>.
/// </summary>
public sealed class KeyboardPushToTalk : IDisposable
{
    private readonly IReadOnlyList<int[]> _keys;
    private readonly HashSet<int> _down = [];
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private LowLevelKeyboardProc? _proc; // kept alive while the hook is installed

    /// <param name="keys">From <see cref="Parse"/>: each entry lists the key codes that satisfy one key of the combination.</param>
    private KeyboardPushToTalk(IReadOnlyList<int[]> keys) => _keys = keys;

    /// <summary>Down (true) or up (false); raised on the hook thread.</summary>
    public event Action<bool>? Changed;

    public bool Held { get; private set; }

    /// <summary>Starts listening for <paramref name="spec"/>; null when the text names no key.</summary>
    public static KeyboardPushToTalk? Start(string? spec)
    {
        if (Parse(spec) is not { } keys) return null;
        var ptt = new KeyboardPushToTalk(keys);
        using var ready = new ManualResetEventSlim();
        ptt._thread = new Thread(() => ptt.Loop(ready)) { IsBackground = true, Name = "Keyboard push-to-talk" };
        ptt._thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
        return ptt;
    }

    private static readonly Dictionary<string, int[]> Names = BuildNames();

    private static Dictionary<string, int[]> BuildNames()
    {
        var n = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = [0xA2, 0xA3], ["Control"] = [0xA2, 0xA3], ["LCtrl"] = [0xA2], ["RCtrl"] = [0xA3],
            ["Shift"] = [0xA0, 0xA1], ["LShift"] = [0xA0], ["RShift"] = [0xA1],
            ["Alt"] = [0xA4, 0xA5], ["LAlt"] = [0xA4], ["RAlt"] = [0xA5], ["AltGr"] = [0xA5],
            ["Space"] = [0x20], ["Tab"] = [0x09], ["Enter"] = [0x0D], ["CapsLock"] = [0x14], ["Pause"] = [0x13],
            ["ScrollLock"] = [0x91], ["Insert"] = [0x2D], ["Delete"] = [0x2E], ["Home"] = [0x24], ["End"] = [0x23],
            ["PageUp"] = [0x21], ["PageDown"] = [0x22], ["Backquote"] = [0xC0],
        };
        for (char c = 'A'; c <= 'Z'; c++) n[c.ToString()] = [c];
        for (char c = '0'; c <= '9'; c++) n[c.ToString()] = [c];
        for (int f = 1; f <= 24; f++) n[$"F{f}"] = [0x6F + f];
        for (int d = 0; d <= 9; d++) n[$"NumPad{d}"] = [0x60 + d];
        return n;
    }

    /// <summary>"Ctrl+Space" gives [[LCtrl, RCtrl], [Space]]; null for empty or unknown names.</summary>
    public static IReadOnlyList<int[]>? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var keys = new List<int[]>();
        foreach (var part in spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Names.TryGetValue(part, out var codes)) return null;
            keys.Add(codes);
        }
        return keys.Count == 0 ? null : keys;
    }

    /// <summary>The combination is held when one code of every key is down.</summary>
    internal static bool IsHeld(IReadOnlyList<int[]> keys, IReadOnlySet<int> down) => keys.All(k => k.Any(down.Contains));

    private void Loop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        ready.Set();
        if (_hook == IntPtr.Zero) return;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            int message = (int)wParam;
            if (message is WM_KEYDOWN or WM_SYSKEYDOWN) _down.Add(vk);
            else if (message is WM_KEYUP or WM_SYSKEYUP) _down.Remove(vk);
            bool held = IsHeld(_keys, _down);
            if (held != Held)
            {
                Held = held;
                // Handlers run on the thread pool, never in the hook: Windows silently removes a low-level hook whose
                // callback is slow (opening the microphone can take a while), and an error here would end the app.
                var changed = Changed;
                if (changed is not null)
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            changed(held);
                        }
                        catch (Exception)
                        {
                            // the host reports its own errors
                        }
                    });
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    private const uint WM_QUIT = 0x12;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
