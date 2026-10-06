using System.Collections.Concurrent;
using RecompOne.Runtime.Host.Window;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// The only writer of the port's settings to interface.ini. See docs/SETTINGS.md.
///
/// The file is the runtime's <c>ViewConfig</c>: a plain dictionary loaded once at
/// boot, and <c>Rt.SaveView</c> writes all of it back and asks ImGui for its layout
/// on the way. Both belong to the host thread. The game's menu runs on the game
/// thread, so a change made there is queued here and written by <see cref="Pump"/>,
/// an invisible panel the host draws every frame.
///
/// A write touches one key. A value the key already holds writes nothing, and a
/// setting put back to its default removes its key rather than writing the
/// default, so a player who never chose follows the default if a release changes it.
/// </summary>
public static class SettingsStore
{
    /// <summary>A setting's new value, or null to remove its key (the default).</summary>
    public readonly record struct Change(PortSetting Setting, double? Value);

    static readonly ConcurrentQueue<(Change[] Changes, string From)> _queue = new();
    static int _hostThread = -1;

    public static long Writes { get; private set; }

    static bool OnHost => Environment.CurrentManagedThreadId == _hostThread;

    public static void Install() => PanelManager.Register(Pump.Instance);

    /// <summary>The Settings window's write, when a control is let go.</summary>
    public static void Write(PortSetting s, double value) => Submit([new(s, value)], "settings window");

    /// <summary>From any thread: written now on the host thread, else on its next frame.</summary>
    public static void Submit(IReadOnlyList<Change> changes, string from)
    {
        if (changes.Count == 0) return;
        if (OnHost) Commit([.. changes], from);
        else _queue.Enqueue(([.. changes], from));
    }

    static void Commit(Change[] changes, string from)
    {
        var values = Rt.View.Values;
        bool changed = false;
        foreach (var (s, value) in changes)
        {
            if (value is double v)
            {
                string text = s.Encode(v);
                if (values.TryGetValue(s.Key, out var old) && old == text) continue;
                values[s.Key] = text;
                Console.WriteLine($"[KF3] settings: {s.Key}={text} ({from})");
            }
            else
            {
                if (!values.Remove(s.Key)) continue;
                Console.WriteLine($"[KF3] settings: {s.Key} back to its default ({from})");
            }
            changed = true;
        }
        if (!changed) return;
        Rt.SaveView();
        Writes++;
    }

    /// <summary>The key's text, read on the host thread; null when no key is kept.</summary>
    public static string? StoredText(PortSetting s) =>
        OnHost && Rt.View.Values.TryGetValue(s.Key, out var text) ? text : null;

    /// <summary>Never shown: the host draws every open panel each frame, which is the
    /// one per-frame call a patch gets on the host thread without a runtime change.</summary>
    sealed class Pump : IFloatingPanel
    {
        public static readonly Pump Instance = new();

        public string Name => "kf3settingsstore";
        public bool IsOpen { get => true; set { } }

        public void Draw()
        {
            _hostThread = Environment.CurrentManagedThreadId;
            while (_queue.TryDequeue(out var item)) Commit(item.Changes, item.From);
        }
    }
}
