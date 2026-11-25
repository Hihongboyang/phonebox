#if WINDOWS
using System.Runtime.InteropServices;
using System.Windows.Forms;
#endif

namespace PhoneBox.Agent;

/// <summary>
/// One global keyboard shortcut for the agent (the Translate hotkey). Windows: registered with RegisterHotKey on a
/// message-only window that lives on an STA thread of its own, so <see cref="Pressed"/> arrives on that thread.
/// Everywhere else nothing is registered and <see cref="Pressed"/> never fires, but the class exists, so the rest of
/// the agent needs no platform branches (the way <see cref="Clipboard"/> is built).
/// </summary>
public sealed class Hotkeys : IHostedService
{
    public const string Registered = "registered";
    public const string Taken = "taken";
    public const string Invalid = "invalid";
    public const string Off = "off";

    /// <summary>Raised each time the hotkey is pressed. On Windows, on the hotkey's own thread.</summary>
    public event Action? Pressed;

    /// <summary>Where the last <see cref="Apply"/> left the hotkey: <see cref="Registered"/>, <see cref="Taken"/>, <see cref="Invalid"/> or <see cref="Off"/>.</summary>
    public string State { get; private set; } = Off;

    private static readonly string[] Modifiers = ["ctrl", "alt", "shift", "win"];

    /// <summary>
    /// Registers the hotkey in place of the one before it, or unregisters it when <paramref name="spec"/> is null.
    /// Returns null, or why it could not be done: <see cref="Invalid"/> for a spec that is not a hotkey, and
    /// <see cref="Taken"/> when Windows would not give the key away (another program owns it).
    /// </summary>
    public string? Apply(string? spec)
    {
        var key = spec is null ? null : Parse(spec);
        if (spec is not null && key is null)
        {
#if WINDOWS
            Ask(null);
#endif
            State = Invalid;
            return Invalid;
        }
#if WINDOWS
        if (_window is null) { State = Off; return null; } // the thread never started: no hotkey to have
        var failed = Ask(key);
        State = failed ? Taken : key is null ? Off : Registered;
        return failed ? Taken : null;
#else
        // outside Windows there are no global hotkeys: nothing can be registered, whatever the spec says
        State = Off;
        return null;
#endif
    }

    /// <summary>
    /// The Windows modifier bits and virtual-key code of a spec such as "ctrl+alt+d" (one or more of ctrl, alt, shift,
    /// win, then exactly one other key), or null when the spec is not a hotkey.
    /// </summary>
    public static (uint Modifiers, uint VirtualKey)? Parse(string spec)
    {
        List<List<string>> combos;
        try { combos = KeySender.Parse(spec); }
        catch (ArgumentException) { return null; } // an unknown key, two keys, or nothing at all
        if (combos.Count != 1) return null; // one hotkey, not a sequence of keys
        var keys = combos[0];
        // KeySender accepts a combo of modifiers alone (it is a valid thing to press); a hotkey needs more
        var key = keys.Where(k => !Modifiers.Contains(k)).ToList();
        var mods = keys.Where(Modifiers.Contains).Distinct().ToList();
        if (key.Count != 1 || mods.Count == 0) return null;
        uint bits = 0;
        foreach (var m in mods) bits |= m switch { "alt" => 0x0001u, "ctrl" => 0x0002u, "shift" => 0x0004u, _ => 0x0008u };
        return (bits, KeySender.WinVk(key[0]));
    }

    public Task StartAsync(CancellationToken ct)
    {
#if WINDOWS
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        // the window exists after this, or the hotkey is simply never registered (Apply says "off")
        ready.Task.Wait(TimeSpan.FromSeconds(5));
#endif
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
#if WINDOWS
        _window?.Post(Window.WmClose); // unregisters, destroys the window and ends the thread's loop
#endif
        return Task.CompletedTask;
    }

#if WINDOWS
    private const int Id = 0xB0D;
    /// <summary>MOD_NOREPEAT: holding the key down must not fire the hotkey again and again.</summary>
    private const uint ModNorepeat = 0x4000;

    private Thread? _thread;
    private ApplicationContext? _ctx;
    private volatile Window? _window;
    private (uint Modifiers, uint VirtualKey)? _wanted;
    private (uint Modifiers, uint VirtualKey)? _registered;

    private void Run(TaskCompletionSource ready)
    {
        _window = new Window(this);
        _ctx = new ApplicationContext();
        ready.SetResult();
        Application.Run(_ctx); // pumps the hotkey messages until the context's thread is told to exit
    }

    /// <summary>Swaps the registration on the window's thread, which is the thread that owns the hotkey. True when Windows refused the key.</summary>
    private bool Ask((uint Modifiers, uint VirtualKey)? key)
    {
        var window = _window!;
        _wanted = key;
        return SendMessage(window.Handle, Window.WmApply, 0, 0) == (nint)1;
    }

    /// <summary>On the window's thread: drop the registration, if there is one.</summary>
    private void Unregister()
    {
        if (_registered is not { } old) return;
        UnregisterHotKey(_window!.Handle, Id);
        _registered = null;
    }

    /// <summary>On the window's thread: drop the old registration and register the wanted key. True when Windows refused the key.</summary>
    private bool Swap()
    {
        Unregister();
        if (_wanted is not { } key) return false;
        if (!RegisterHotKey(_window!.Handle, Id, key.Modifiers | ModNorepeat, key.VirtualKey)) return true;
        _registered = key;
        return false;
    }

    /// <summary>
    /// A message-only window (HWND_MESSAGE): nothing on screen and nothing in the taskbar, and it receives only the
    /// messages aimed at it — the hotkey, and the Apply and close requests handed over from other threads.
    /// </summary>
    private sealed class Window : NativeWindow
    {
        public const int WmHotkey = 0x0312, WmApply = 0x8001, WmClose = 0x8002;

        private readonly Hotkeys owner;

        public Window(Hotkeys owner)
        {
            this.owner = owner;
            CreateHandle(new CreateParams { Caption = "PhoneBoxAgentHotkeys", Parent = (nint)(-3) /* HWND_MESSAGE */ });
        }

        public void Post(int msg) => PostMessage(Handle, msg, 0, 0);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam == (nint)Id)
            {
                owner.Pressed?.Invoke();
                return;
            }
            if (m.Msg == WmApply)
            {
                m.Result = owner.Swap() ? (nint)1 : (nint)0;
                return;
            }
            if (m.Msg == WmClose)
            {
                owner.Unregister();
                DestroyHandle();
                owner._ctx?.ExitThread();
                return;
            }
            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);
#endif
}
