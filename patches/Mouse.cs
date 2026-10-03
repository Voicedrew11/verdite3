using ImGuiNET;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using Silk.NET.Input;
// Both namespaces above define a MouseButton; the host's input one is the one
// HostWindow.IsMouseButtonDown takes.
using MouseButton = Silk.NET.Input.MouseButton;

namespace Kf3;

/// <summary>
/// Mouse look, and the mouse buttons, as a third source the game's own control
/// velocities can be driven from. Ported from Verdite2's Kf2.Mouse; this game's
/// values are in the block below.
///
///     KF3_MOUSE=1                              on; on by default
///     KF3_MOUSE_TURN=1.0 KF3_MOUSE_LOOK=1.0    sensitivities
///     KF3_MOUSE_INVERTY=1                      look-Y inversion
///     KF3_MOUSE_LEAD=0                         show motion when the tick spends it
///     KF3_MOUSE_BUTTONS=Triangle,Square,Circle left, right, middle, as pad buttons
///     KF3_MOUSE_KEY=Escape                     the key that captures and releases
///
/// The knobs are settings, in the Gameplay tab.
///
/// **The look half is not its own hook.** In Verdite2 a mouse is another way of
/// choosing the per-frame turn and pitch step, and Analog.BeforeLook owned that
/// word: this class handed that hook a number and nothing else. Here the hook is
/// patches/MouseLook.cs: it spends <see cref="TakeLook"/> and reports what it
/// asked for through <see cref="NoteSpent"/>.
///
/// Three things make a mouse different from a stick, and they are the whole
/// design:
///
/// * **A mouse gives displacement, not a rate.** A stick deflection means "turn
///   at this speed for as long as I hold it"; mouse motion means "turn by this
///   much, once". So there is no deadzone, no response curve and no acceleration
///   ramp here — those all shape a held rate — and the pixels go straight to an
///   angle at <see cref="DegreesPerPixel"/> a pixel.
/// * **It has to put the axis down the moment it stops.** The game ramps a
///   released look velocity down over about eleven frames, which on a mouse reads
///   as the camera sliding on after the hand has stopped. The hook must do that
///   itself; the mouse's release is not optional.
/// * **The pointer runs out of desktop.** Motion is only motion while the cursor
///   is locked to the window, so the mouse does nothing at all until it is
///   captured — <see cref="CaptureKey"/>, Escape by default, and any popup taking
///   the pointer back.
///
/// The buttons take the other route entirely: <c>PadReadEvent</c> fires inside
/// the BIOS's PAD_dr, so a held mouse button is ORed into the word the game is
/// about to read, as the pad button the player picked. Nothing here knows what
/// "attack" is — the game's own control-config screen decides that, exactly as
/// it does for the pad. It is also why the buttons work in menus, on the title
/// screen and anywhere else the game reads the pad, without a hook of its own.
/// </summary>
public static class Mouse
{
    // This game's values: the look routine func_8002F5C0, see patches/MouseLook.cs.
    const float UnitsPerDegree = 4096f / 360f;  // 12 bits to yaw's circle
    const float DegreesPerPixel = 0.15f;        // a quarter turn is about 600 px at sensitivity 1
    internal const int StepCap = 1024;          // the most one tick may turn, in yaw units
    internal const int PitchLimit = 0x2BC;              // func_8002F5C0's limit, 0x2BC and 0xD44 at 12 bits
    const uint YawAddress = 0x801B2612;         // u16, the base yaw the look routine accumulates
    internal const uint PitchAddress = 0x801B2610; // u16, the base pitch, a 12-bit angle
    const int DefaultLeftButton = 4;            // Triangle: attack
    const int DefaultRightButton = 3;           // Square: magic
    const int DefaultMiddleButton = 2;          // Circle: examine, open, talk

    /// <summary>
    /// Motion older than this is thrown away rather than applied.
    ///
    /// The accumulator fills whenever the pointer moves, and the routine that
    /// spends it only runs while the game is walking around: the in-game menu
    /// blocks inside its own call for as long as it is open, an area load takes
    /// seconds. Coming back out of one of those with every pixel moved in the
    /// meantime still queued would swing the camera through whatever the player
    /// did with their hand while reading a menu.
    /// </summary>
    const long StaleMs = 250;

    // ---- settings ---------------------------------------------------------------
    //
    // Plain public fields, for the reason Analog's are: every one of them is a
    // widget's `ref` argument.

    public const string OnKey      = "kf3.mouse.on";
    public const string TurnKey    = "kf3.mouse.turn";
    public const string LookKey    = "kf3.mouse.look";
    public const string InvertKey  = "kf3.mouse.inverty";
    public const string LeftKey    = "kf3.mouse.left";
    public const string RightKey   = "kf3.mouse.right";
    public const string MiddleKey  = "kf3.mouse.middle";
    public const string CaptureKeyKey = "kf3.mouse.capturekey";
    public const string LeadKey    = "kf3.mouse.lead";

    /// <summary>
    /// On by default. Capture is still Escape, so a pointer does not disappear
    /// into the game until the player asks; once they do, look and the mouse
    /// buttons are already wired.
    /// </summary>
    public static bool Enabled = true;

    /// <summary>
    /// Show mouse look the frame it happens rather than when the next tick spends
    /// it. On by default; <c>KF3_MOUSE_LEAD=0</c> is the comparison.
    /// </summary>
    public static bool Lead = true;

    public static float TurnSens = 1.0f;
    public static float LookSens = 1.0f;
    public static bool InvertY;

    /// <summary>Indices into <see cref="PadButtons"/>, in the order left, right,
    /// middle. The defaults are attack, magic and examine.</summary>
    public static int LeftButton = DefaultLeftButton;
    public static int RightButton = DefaultRightButton;
    public static int MiddleButton = DefaultMiddleButton;

    /// <summary>
    /// What each mouse button presses, as a pad button rather than as an action.
    ///
    /// The names here are the pad's, not the game's, because that is the truth:
    /// this presses a button and the game's control-config screen decides what
    /// the button does.
    /// </summary>
    internal static readonly (string Name, ushort Bit)[] PadButtons =
    [
        ("None",     0),
        ("Cross",    Controller.Cross),
        ("Circle",   Controller.Circle),
        ("Square",   Controller.Square),
        ("Triangle", Controller.Triangle),
        ("L1",       Controller.L1),
        ("R1",       Controller.R1),
        ("L2",       Controller.L2),
        ("R2",       Controller.R2),
        ("Start",    Controller.Start),
        ("Select",   Controller.Select),
    ];

    /// <summary>
    /// The key that locks the pointer to the window and gives it back.
    ///
    /// Escape by default: the game's own keymap has to leave it free, and it
    /// stays out of the way of the runtime's own use of it — a popup closes on
    /// Escape, so while one is open this leaves the key alone.
    /// </summary>
    public static Key CaptureKey = Key.Escape;

    /// <summary>The keys the settings page offers, since it has no key-capture
    /// widget of its own.</summary>
    internal static readonly Key[] CaptureKeys =
        [Key.Escape, Key.Tab, Key.GraveAccent, Key.F9, Key.F10, Key.F12];

    /// <summary>Whether the pointer is locked to the window right now. The host
    /// is the authority — a platform that refuses the mode leaves this
    /// false.</summary>
    public static bool Captured { get; private set; }

    static long _taken;
    static long _checked;
    static bool _hinted;

    static readonly HashSet<string> _fromEnv = new(StringComparer.Ordinal);

    public static void Configure()
    {
        Env("KF3_MOUSE", OnKey, ref Enabled, _fromEnv);
        Env("KF3_MOUSE_TURN", TurnKey, ref TurnSens, _fromEnv);
        Env("KF3_MOUSE_LOOK", LookKey, ref LookSens, _fromEnv);
        Env("KF3_MOUSE_INVERTY", InvertKey, ref InvertY, _fromEnv);
        Env("KF3_MOUSE_LEAD", LeadKey, ref Lead, _fromEnv);

        // One variable for the three buttons rather than three: they are set
        // together or not at all, and "Triangle,Square,Circle" says what it does.
        string? buttons = Environment.GetEnvironmentVariable("KF3_MOUSE_BUTTONS");
        if (!string.IsNullOrWhiteSpace(buttons))
        {
            var names = buttons.Split(',', StringSplitOptions.TrimEntries);
            for (int i = 0; i < names.Length && i < 3; i++)
            {
                int index = Array.FindIndex(PadButtons,
                    b => string.Equals(b.Name, names[i], StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    throw new ArgumentException(
                        $"KF3_MOUSE_BUTTONS: no pad button '{names[i]}'; one of " +
                        string.Join(", ", PadButtons.Select(b => b.Name)));

                if (i == 0) { LeftButton = index; _fromEnv.Add(LeftKey); }
                else if (i == 1) { RightButton = index; _fromEnv.Add(RightKey); }
                else { MiddleButton = index; _fromEnv.Add(MiddleKey); }
            }
        }

        string? key = Environment.GetEnvironmentVariable("KF3_MOUSE_KEY");
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (!Enum.TryParse<Key>(key.Trim(), true, out var parsed))
                throw new ArgumentException($"KF3_MOUSE_KEY: no key '{key}' (a Silk.NET key name)");
            CaptureKey = parsed;
            _fromEnv.Add(CaptureKeyKey);
        }
    }

    /// <summary>
    /// Listeners, and no hook of its own: the look half is spent by the game's
    /// own look routine through the survey's seam, and the button half rides on
    /// the bus the BIOS fires when the game reads the pad.
    /// </summary>
    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            // Registered here rather than in Program.cs so it lands after the
            // runtime's own panels: PanelManager draws in registration order,
            // and a capture announcement belongs over the game rather than under
            // it. It is never persisted -- see MouseIndicator.IsOpen.
            PanelManager.Register(MouseIndicator.Instance);

            Saved(OnKey, ref Enabled, _fromEnv);
            Saved(TurnKey, ref TurnSens, _fromEnv);
            Saved(LookKey, ref LookSens, _fromEnv);
            Saved(InvertKey, ref InvertY, _fromEnv);
            Saved(LeadKey, ref Lead, _fromEnv);
            Saved(LeftKey, ref LeftButton, _fromEnv);
            Saved(RightKey, ref RightButton, _fromEnv);
            Saved(MiddleKey, ref MiddleButton, _fromEnv);

            int key = (int)CaptureKey;
            Saved(CaptureKeyKey, ref key, _fromEnv);
            if (Enum.IsDefined(typeof(Key), key)) CaptureKey = (Key)key;

            LeftButton = Clamp(LeftButton);
            RightButton = Clamp(RightButton);
            MiddleButton = Clamp(MiddleButton);

            if (Enabled)
                Console.WriteLine($"[KF3] mouse: on, {CaptureKey} captures the pointer " +
                                  $"(turn x{TurnSens:0.##}, look x{LookSens:0.##}; " +
                                  $"{PadButtons[LeftButton].Name}/{PadButtons[RightButton].Name}/" +
                                  $"{PadButtons[MiddleButton].Name} on left/right/middle)");
        });

        // The capture key comes off the event bus rather than being polled, and
        // that is deliberate: the polled route would have to live in a hook, and
        // every hook this port owns is in the walking-around part of the game. A
        // pointer captured and then swallowed by the in-game menu has to be
        // releasable from inside it.
        Event.AddListener<KeyboardEvent>(e =>
        {
            if (!e.Pressed || e.Key != (int)CaptureKey) return;
            if (!Enabled) return;

            // A popup is drawn over the game and closes on Escape itself, so the
            // key belongs to it while one is open. Releasing is still allowed --
            // that is the popup taking the pointer back, below.
            if (!Captured && PopupManager.AnyOpen) return;
            if (!Captured && WantTextInput) return;

            SetCaptured(!Captured);
        });

        // The buttons are not listened for here. PAD_dr is the busiest call in
        // this game -- the screen transitions busy-wait on it, hundreds of
        // thousands of times a second -- and a listener on that bus is a cost
        // every player pays for a device most of them are not using. It is
        // attached when the pointer is captured and dropped when it is let go,
        // which is exactly the window in which a mouse button means anything.
    }

    /// <summary>A letter hotkey waits while ImGui has a text field focused. The
    /// remaster editor doesn't exist in this port, so this is the whole gate.</summary>
    static bool WantTextInput =>
        ImGui.GetCurrentContext() != nint.Zero && ImGui.GetIO().WantTextInput;

    /// <summary>
    /// The mouse buttons, ORed into the word the game is about to read. Attached
    /// only while the pointer is captured (see <see cref="Install"/>).
    /// </summary>
    static readonly Action<PadReadEvent> _buttons = e =>
    {
        if (e.Port != 0 || !Enabled || !Captured) return;

        // A popup is drawn over a running game and wants a cursor, so opening
        // one takes the pointer back. This is the only state change nothing
        // else can announce, and PAD_dr is the one thing the game keeps doing
        // wherever it is -- in a menu, on a loading screen, in the ending.
        Watch();
        if (!Captured) return;

        ushort press = 0;
        if (HostWindow.IsMouseButtonDown(MouseButton.Left)) press |= PadButtons[LeftButton].Bit;
        if (HostWindow.IsMouseButtonDown(MouseButton.Right)) press |= PadButtons[RightButton].Bit;
        if (HostWindow.IsMouseButtonDown(MouseButton.Middle)) press |= PadButtons[MiddleButton].Bit;
        if (press == 0) return;

        // The buffer PAD_dr fills is active low and carries the two button bytes
        // the other way round from Controller's layout -- libetc hands the game
        // `~buffer`, and the game's own mask table is stored swapped for the same
        // reason. Clearing the swapped bit here is what the game reads back as
        // "pressed". Same shape as AutoStart's injection.
        e.Buttons &= (ushort)~(ushort)((press >> 8) | (press << 8));
    };

    // Motion drained from the host but not yet spent by a tick, in game units.
    // Poll drains it every drawn frame so a lead can show it before the tick
    // lands; the look routine spends it (TakeLook).
    static float _pendTurn, _pendPitch;
    static long _polled;

    // What the last tick asked for, and the base angles before the game applied
    // it, so a lead can measure what the game actually turned by.
    static long _takenFrame = -1;
    static float _spentTurn, _spentPitch;
    static float _shareTurn, _sharePitch;
    static ushort _yawBefore, _pitchBefore;

    /// <summary>
    /// Drain the host's motion into the pending sum. Called every drawn frame
    /// from the stage that carries the view, and again by <see cref="TakeLook"/>.
    /// Motion is dropped rather than kept while the look routine is not running
    /// (a menu, a load, the paused map), for the reason <see cref="StaleMs"/>
    /// gives.
    /// </summary>
    internal static void Poll()
    {
        // Pumped here, so every frame's motion is current to this point: left to
        // the present's pump and the pad read's, a tick frame samples after the
        // pacing wait and the rest before it, which chops a steady turn into
        // three unequal pieces. Only while captured, so it costs nothing
        // otherwise.
        if (Enabled && Captured) HostWindow.PumpInput(0.0);

        long now = Environment.TickCount64;
        var (dx, dy) = HostWindow.TakeMouseMotion();
        long gap = now - _polled;
        _polled = now;

        if (!Enabled || !Captured || gap > StaleMs || !Live(now))
        {
            _pendTurn = _pendPitch = 0f;
            return;
        }

        const float units = DegreesPerPixel * UnitsPerDegree;
        _pendTurn += -dx * units * TurnSens;
        _pendPitch += dy * units * LookSens * (InvertY ? -1f : 1f);
    }

    /// <summary>Whether the look routine has spent the mouse recently enough
    /// that motion now will be spent too: three ticks, or
    /// <see cref="StaleMs"/> if that is shorter. Verdite2 also
    /// dropped motion while the world was paused.</summary>
    static bool Live(long now) =>
        now - _taken <= Math.Min(StaleMs, (long)(3000.0 / Math.Max(1.0, FramePacing.LogicHz)));

    /// <summary>
    /// Motion drawn but not yet spent, capped as the look routine will cap it:
    /// what the next tick will turn by, as far as the mouse is concerned. Zero
    /// unless the look routine is live.
    /// </summary>
    internal static (float Turn, float Pitch) Pending
    {
        get
        {
            if (!Enabled || !Captured || !Live(Environment.TickCount64)) return (0f, 0f);
            return (Math.Clamp(_pendTurn, -StepCap, StepCap),
                    Math.Clamp(_pendPitch, -StepCap, StepCap));
        }
    }

    /// <summary>
    /// The motion since the last tick, in the game's own angle units and in its
    /// own sign convention. Both zero unless the pointer is captured.
    ///
    /// Called once a tick from the survey's look hook, and it empties the pending
    /// sum whether or not it is going to use it — motion collected while the
    /// mouse was doing something else is not a turn anybody asked for.
    /// </summary>
    internal static (float Turn, float Pitch) TakeLook()
    {
        if (!Captured && Enabled && !_hinted)
        {
            // Said once, and here rather than at boot: this runs from the game's
            // own look routine, so reaching it means the player is walking around
            // with mouse look on and a pointer that is still a pointer. The cut
            // mouse is the whole of the answer.
            _hinted = true;
            MouseIndicator.Show(false);
        }

        Poll();
        var take = (_pendTurn, _pendPitch);
        _pendTurn = _pendPitch = 0f;
        _taken = Environment.TickCount64;
        _takenFrame = FramePacing.Frames;
        _spentTurn = _spentPitch = 0f;
        _shareTurn = _sharePitch = 0f;
        return take;
    }

    /// <summary>
    /// What the survey's look hook asked the game for this tick: the base angles
    /// before, and the mouse's share of each axis's step. The rest of the step
    /// was a stick's.
    /// </summary>
    internal static void NoteSpent(IMemory m, float turn, float stickTurn, float pitch, float stickPitch)
    {
        _yawBefore = m.ReadU16(YawAddress);
        _pitchBefore = m.ReadU16(PitchAddress);
        _spentTurn = turn;
        _spentPitch = pitch;
        _shareTurn = Share(turn, stickTurn);
        _sharePitch = Share(pitch, stickPitch);
    }

    static int Delta12(ushort to, ushort from)
    {
        int d = (to - from) & 0xFFF;
        return d >= 0x800 ? d - 0x1000 : d;
    }

    static float Share(float mouse, float stick) =>
        mouse == 0f ? 0f : stick == 0f ? 1f : Math.Clamp(mouse / (mouse + stick), 0f, 1f);

    /// <summary>
    /// What the game turned by for the mouse on the tick spent in frame
    /// <see cref="FramePacing.Frames"/>, measured off the base angles rather than
    /// assumed, so the pitch limit and anything else the game did to the step are
    /// in it. Null when the look routine did not spend the mouse on this frame.
    /// Also returns what was asked for, which a probe can compare.
    /// </summary>
    internal static (int Yaw, int Pitch, float AskedYaw, float AskedPitch)? SpentThisFrame(IMemory m)
    {
        if (_takenFrame != FramePacing.Frames || (_shareTurn == 0f && _sharePitch == 0f)) return null;

        // Both are 12-bit angles, pitch included: the look routine stores it
        // `& 0xFFF`, so looking just above level reads 0x0FFx.
        int dYaw = Delta12(m.ReadU16(YawAddress), _yawBefore);
        int dPitch = Delta12(m.ReadU16(PitchAddress), _pitchBefore);

        return ((int)MathF.Round(dYaw * _shareTurn), (int)MathF.Round(dPitch * _sharePitch),
                _spentTurn, _spentPitch);
    }

    /// <summary>
    /// Lock or release, and say so on screen -- through Verdite Core's MouseIndicator
    /// rather than through a toast, since this is a state a player changes while
    /// playing. The failure below keeps its toast: it is rare, it is not a state,
    /// and it needs words.
    ///
    /// Reads the host back rather than trusting the write: no mouse, or a
    /// platform without the cursor mode, and the answer is no.
    /// </summary>
    public static void SetCaptured(bool on)
    {
        if (on == Captured) return;

        HostWindow.MouseCaptured = on;
        Captured = HostWindow.MouseCaptured;

        if (Captured) Event.AddListener(_buttons);
        else Event.RemoveListener(_buttons);

        if (on && !Captured)
        {
            Console.Error.WriteLine("[KF3] mouse: the host will not lock the pointer; mouse look is inert");
            ToastNotifications.ShowText("Mouse look", "This display cannot lock the pointer");
            return;
        }

        // Taking the pointer back has to clear the accumulator too: the jump from
        // wherever the cursor was left is not motion.
        HostWindow.TakeMouseMotion();
        _pendTurn = _pendPitch = 0f;
        _taken = _polled = Environment.TickCount64;

        MouseIndicator.Show(Captured);
    }

    /// <summary>
    /// The one state change nothing announces: a popup opening while the pointer
    /// is captured. Settings, the mods list and the disc picker are all drawn over
    /// a running game and all want a cursor, so opening one gives it back.
    ///
    /// Throttled to once a millisecond, because its caller is PAD_dr.
    /// </summary>
    static void Watch()
    {
        long now = Environment.TickCount64;
        if (now == _checked) return;
        _checked = now;

        if (Captured && PopupManager.AnyOpen) SetCaptured(false);
    }

    static int Clamp(int index) => index < 0 || index >= PadButtons.Length ? 0 : index;

    // ---- environment, then interface.ini ------------------------------------
    //
    // Same precedence as TestingSection: a variable set wins over the value kept
    // in interface.ini, which is read once the config has loaded.

    static void Env(string name, string key, ref bool value, HashSet<string> from)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return;
        value = v.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";
        from.Add(key);
    }

    static void Env(string name, string key, ref float value, HashSet<string> from)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return;
        if (!float.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float f))
            throw new ArgumentException($"{name}: cannot read '{v}'");
        value = f;
        from.Add(key);
    }

    /// <summary>The saved value, unless the environment already set this key.
    /// Stored as 0/1, the shape TestingSection keeps its switches in, so the
    /// Testing tab and the Gameplay page agree on the same key.</summary>
    static void Saved(string key, ref bool value, HashSet<string> from)
    {
        if (!from.Contains(key))
            value = RecompOne.Runtime.Runtime.View.GetInt(key, value ? 1 : 0) != 0;
    }

    static void Saved(string key, ref float value, HashSet<string> from)
    {
        if (!from.Contains(key)) value = RecompOne.Runtime.Runtime.View.GetFloat(key, value);
    }

    static void Saved(string key, ref int value, HashSet<string> from)
    {
        if (!from.Contains(key)) value = RecompOne.Runtime.Runtime.View.GetInt(key, value);
    }
}
