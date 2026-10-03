// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3.Mods.Debug;

/// <summary>
/// Position bookmarks and area warp.
///
/// Two very different operations. A bookmark is six words -- it puts the player
/// somewhere else in the area that is already loaded, and nothing else changes.
/// An area warp runs the game's own area-entry routine, which unloads the
/// current area module, loads another off the disc and re-enters it; that is a
/// real state transition and it can fail in ways a bookmark cannot.
///
/// Unlike the KF2 original, a bookmark here remembers the area it was taken in.
/// The descriptor at 0x8018FAE4..E8 carries the area index in its first byte
/// (FDAT entries 3n..3n+2), and the disc's own area names are not known, so each
/// area is shown as "fdatNN (area n)". Recalling a bookmark from another area
/// therefore becomes an area warp followed by the placement.
/// </summary>
internal static class Warp
{
    internal const int SlotCount = 4;

    // 28 area modules: FDAT entries 3n+2 for n = 0..27, named fdat02, fdat05,
    // ..., fdat83. See docs and findings/areas.md. n = 0 is fdat02.
    internal const int AreaCount = 28;

    // 0x8018FAD4, s16: pending area change / load in progress. func_80017C78
    // sets it to 1; main-loop stage 8 func_80018358 clears it when the load is
    // complete. The game's own exit handlers spin on it.
    const uint PendingLoad = 0x8018FAD4;

    // 0x801B25FC, s32: the fourth position word (0 in fdat02). GameState.cs does
    // not expose it and is not ours to edit; the exits zero it, so a recall does.
    const uint PosExtra = 0x801B25FC;

    internal static string Status = "";

    struct Bookmark
    {
        public bool Set;
        public int Area;                 // area index the six words belong to
        public int X, Y, Z, Pitch, Yaw, Roll;
    }

    static readonly Bookmark[] _slots = new Bookmark[SlotCount];

    // ---- queued work ----
    //
    // The panel runs on the UI thread inside Present, which is already inside
    // VSync; the game must not be mutated from there. Everything a button asks
    // for is parked here and run from the hooks below, on the game thread.
    static int _warpArea = -1;      // area to load, -1 when none
    static bool _warpRequested;     // func_80017C78 has been called for _warpArea
    static bool _warpLoaded;        // FAD4 returned to 0; stage 4 does the placement
    static Bookmark _warpTarget;    // where to land after the load; Set=false = default entrance

    static bool _movePending;       // a within-area move is queued
    static bool _moveAngles;        // also restore the angles (bookmark) or keep them (teleport)
    static int _moveX, _moveY, _moveZ, _movePitch, _moveYaw, _moveRoll;

    internal static bool IsSet(int slot) =>
        slot >= 0 && slot < SlotCount && _slots[slot].Set;

    internal static (int X, int Y, int Z) SlotPosition(int slot) =>
        slot >= 0 && slot < SlotCount
            ? (_slots[slot].X, _slots[slot].Y, _slots[slot].Z)
            : (0, 0, 0);

    /// <summary>The area a bookmark was taken in, or -1 for an empty slot.</summary>
    internal static int SlotArea(int slot) =>
        slot >= 0 && slot < SlotCount && _slots[slot].Set ? _slots[slot].Area : -1;

    /// <summary>True while any move is queued or an area load is running; the panel greys its buttons on it.</summary>
    internal static bool Busy => _warpArea >= 0 || _movePending;

    /// <summary>The display name for an area index; the game's names are not known.</summary>
    internal static string AreaName(int area) => $"fdat{3 * area + 2:D2} (area {area})";

    // ---- bookmarks ----

    internal static bool Save(int slot)
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null || !GameState.IsInGame(mem))
        {
            Status = "nothing to bookmark -- no area running";
            return false;
        }
        if (slot < 0 || slot >= SlotCount) return false;

        var (x, y, z) = GameState.Position(mem);
        var (pitch, yaw, roll) = GameState.Angles(mem);

        _slots[slot] = new Bookmark
        {
            Set = true,
            Area = mem.ReadU8(GameState.Area),
            X = x, Y = y, Z = z,
            Pitch = pitch, Yaw = yaw, Roll = roll,
        };

        Persist(slot);
        Status = $"bookmark {slot + 1} saved in {AreaName(_slots[slot].Area)} at {Format(x, y, z)}";
        Console.WriteLine($"[kf3debug] {Status}");
        return true;
    }

    /// <summary>
    /// Put the player back. A bookmark in the area that is loaded writes the
    /// position and angles directly; the reference's six words do this from the
    /// stage-4 post-hook. A bookmark from another area cannot be placed until
    /// that area is in, so it queues a warp first and placement follows the
    /// load. That is the one behaviour the KF2 original did not have: there a
    /// bookmark carried no area and landed at those coordinates in whatever
    /// area you were standing in.
    /// </summary>
    internal static bool Restore(int slot)
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null || !GameState.IsInGame(mem))
        {
            Status = "no area running";
            return false;
        }
        if (slot < 0 || slot >= SlotCount || !_slots[slot].Set)
        {
            Status = $"bookmark {slot + 1} is empty";
            return false;
        }
        if (_warpArea >= 0 || _movePending)
        {
            Status = "a move is already queued";
            return false;
        }

        ref Bookmark b = ref _slots[slot];
        if (b.Area == mem.ReadU8(GameState.Area))
        {
            _movePending = true;
            _moveAngles = true;
            _moveX = b.X; _moveY = b.Y; _moveZ = b.Z;
            _movePitch = b.Pitch; _moveYaw = b.Yaw; _moveRoll = b.Roll;
            Status = $"recalling bookmark {slot + 1} at {Format(b.X, b.Y, b.Z)}...";
            Console.WriteLine($"[kf3debug] {Status}");
        }
        else
        {
            _warpArea = b.Area;
            _warpTarget = b;
            _warpRequested = false;
            _warpLoaded = false;
            Status = $"warping to {AreaName(b.Area)} to recall bookmark {slot + 1}...";
            Console.WriteLine($"[kf3debug] queued warp to {AreaName(b.Area)} for bookmark {slot + 1}");
        }
        return true;
    }

    internal static bool Teleport(int x, int y, int z)
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null || !GameState.IsInGame(mem))
        {
            Status = "no area running";
            return false;
        }
        if (_warpArea >= 0 || _movePending)
        {
            Status = "a move is already queued";
            return false;
        }

        _movePending = true;
        _moveAngles = false;
        _moveX = x; _moveY = y; _moveZ = z;
        Status = $"moving to {Format(x, y, z)}...";
        Console.WriteLine($"[kf3debug] {Status}");
        return true;
    }

    // ---- area warp ----

    /// <summary>
    /// Queue an area warp to the area's default entrance.
    ///
    /// The load itself is func_80017C78, the game's own warp primitive: the
    /// exits (kind 0xE0/0xEB objects in stage 3), save respawn and the menu load
    /// all go through it. It releases the area's objects, writes the descriptor
    /// at 0x8018FAE4, sets 0x8018FAD4 = 1 and lets main-loop stage 8
    /// (func_80018358) drive the disc read. It does not itself place the player.
    /// </summary>
    internal static bool ToArea(int area)
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null || !GameState.IsInGame(mem))
        {
            Status = "no area running -- load a save first";
            return false;
        }
        // The KF2 original refused its one cut area (fdat32). Here every one of
        // the 28 modules is a real area -- fdat32 is n = 16 -- so there is
        // nothing to refuse beyond a bad index.
        if (area < 0 || area >= AreaCount)
        {
            Status = $"area {area} does not exist";
            return false;
        }
        if (_warpArea >= 0 || _movePending)
        {
            Status = "a move is already queued";
            return false;
        }

        _warpArea = area;
        _warpTarget = default;
        _warpRequested = false;
        _warpLoaded = false;
        Status = $"warping to {AreaName(area)} (default entrance)...";
        Console.WriteLine($"[kf3debug] queued warp to {AreaName(area)}");
        return true;
    }

    /// <summary>
    /// End of main-loop stage 3, the object walk (func_80047010). This is the
    /// site the game's own transitions run from -- kind-0xE0 and 0xEB objects
    /// call func_80017C78 inside this routine -- so it is the one place the
    /// loader may be started from. Starting it from the panel nested rendering
    /// under a frame that was still drawing the area that had just unloaded.
    ///
    /// The request is started on the first pass and the load is left to stage 8;
    /// a later pass sees FAD4 back at 0 and hands the placement to stage 4.
    /// </summary>
    [PostHook("game", Address = 0x80047010)]
    static void AfterObjectWalk(CpuContext c, IMemory m)
    {
        if (_warpArea < 0) return;

        if (!GameState.IsInGame(m))
        {
            // The area went away under us (returned to the attract loop); drop
            // the request rather than touch a block that is not a character.
            CancelWarp("no area running");
            return;
        }

        if (!_warpRequested)
        {
            Console.WriteLine($"[kf3debug] warping from area {m.ReadU8(GameState.Area)} to area {_warpArea}");
            RequestAreaChange(c, m, _warpArea);
            _warpRequested = true;
        }

        // func_80017C78 may spin the loader itself in one of its paths, or just
        // arm it for stage 8; either way FAD4 == 0 means the area is in.
        if (m.ReadU16(PendingLoad) == 0) _warpLoaded = true;
    }

    /// <summary>
    /// End of main-loop stage 4, the player's tick (func_80030FCC) -- the last
    /// word before stage 10 copies the player into the camera. Both a within-area
    /// recall and the placement after a finished warp are written here, so the
    /// first frame drawn is already at the destination.
    /// </summary>
    [PostHook("game", Address = 0x80030FCC)]
    static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        if (!_movePending && !_warpLoaded) return;

        if (!GameState.IsInGame(m))
        {
            _movePending = false;
            ClearWarpState();
            return;
        }

        if (_warpLoaded)
        {
            int area = _warpArea;
            Bookmark target = _warpTarget;
            ClearWarpState();

            if (target.Set)
            {
                GameState.SetPosition(m, target.X, target.Y, target.Z);
                GameState.SetAngles(m, target.Pitch, target.Yaw, target.Roll);
                GameState.StopMotion(m);
                m.WriteU32(PosExtra, 0);
                Status = $"warped to {AreaName(area)} and recalled bookmark at {Format(target.X, target.Y, target.Z)}";
            }
            else
            {
                // No destination: the area's own spawn placement stands. Say so
                // rather than claiming a position.
                Status = $"warped to {AreaName(area)} (default entrance)";
            }
            Console.WriteLine($"[kf3debug] {Status}");
            return;
        }

        GameState.SetPosition(m, _moveX, _moveY, _moveZ);
        if (_moveAngles) GameState.SetAngles(m, _movePitch, _moveYaw, _moveRoll);
        GameState.StopMotion(m);
        m.WriteU32(PosExtra, 0);
        _movePending = false;
        Status = $"moved to {Format(_moveX, _moveY, _moveZ)}";
        Console.WriteLine($"[kf3debug] {Status}");
    }

    /// <summary>
    /// Call the game's warp primitive with the eight arguments it takes.
    ///
    /// This is the cross-area form the kind-0xEB handler uses at 0x8004A3D8:
    /// func_80017C78(area, area, area, 0xff, 0xff, 0x7f, 0x7f, 0x7f). a0..a3 go
    /// in registers; the remaining four go on the caller's stack at sp+0x10,
    /// +0x14, +0x18, +0x1c, so the stack pointer is moved down first and put
    /// back after. The first five are the descriptor bytes written to
    /// 0x8018FAE4..E8, where 0xff means "keep the pending byte"; the last three
    /// are the entrance offsets FAED/EE/EF, where 0x7f means "no offset".
    ///
    /// The 0xE0 door handler instead passes the destination's own aux/BGM bytes
    /// (object+0x3A..0x3E, e.g. 04 04 04 0D 0F for fdat14). Those select per-area
    /// FDAT data this mod does not know for an arbitrary area, so it uses the
    /// game's own "go to area N" form and lands at the default entrance.
    /// </summary>
    static void RequestAreaChange(CpuContext c, IMemory m, int area)
    {
        var saved = c.Snapshot();
        uint sp = c.SP - 0x20u;
        c.SP = sp;
        c.A0 = (uint)area;
        c.A1 = (uint)area;
        c.A2 = (uint)area;
        c.A3 = 0xFFu;
        m.WriteU32(sp + 0x10u, 0xFFu);   // descriptor byte 4 (FAE8)
        m.WriteU32(sp + 0x14u, 0x7Fu);   // entrance offset X (FAED)
        m.WriteU32(sp + 0x18u, 0x7Fu);   // entrance offset Z (FAEE)
        m.WriteU32(sp + 0x1Cu, 0x7Fu);   // entrance offset height (FAEF)
        Recompiled.KingsField3_game.func_80017C78(c, m);
        c.Restore(saved);
    }

    static void ClearWarpState()
    {
        _warpLoaded = false;
        _warpRequested = false;
        _warpArea = -1;
        _warpTarget = default;
    }

    static void CancelWarp(string reason)
    {
        ClearWarpState();
        Status = reason;
    }

    // Format stands in for Noclip.Format, and the Noclip.Resync() calls the
    // KF2 original made after every write are dropped: this port has no Noclip
    // (only GameState.cs exists), and the stage-4 write is already the last word
    // before the camera is built.
    static string Format(int x, int y, int z) => $"({x}, {y}, {z})";

    internal static void Reset()
    {
        // Nothing is switched on behind us; the bookmarks themselves are the
        // whole point of persisting and are left alone.
        ClearWarpState();
        _movePending = false;
        _moveAngles = false;
    }

    // ---- persistence ----
    //
    // Bookmarks are worth keeping across a restart -- the whole point is to get
    // back somewhere awkward without walking there again. The KF2 original
    // persisted six fields; the area is the seventh this game needs.

    static string Key(int slot, string field) => $"kf3.debug.bookmark{slot}.{field}";

    static void Persist(int slot)
    {
        var view = RecompOne.Runtime.Runtime.View;
        ref Bookmark b = ref _slots[slot];
        view.SetBool(Key(slot, "set"), b.Set);
        view.SetInt(Key(slot, "area"), b.Area);
        view.SetInt(Key(slot, "x"), b.X);
        view.SetInt(Key(slot, "y"), b.Y);
        view.SetInt(Key(slot, "z"), b.Z);
        view.SetInt(Key(slot, "pitch"), b.Pitch);
        view.SetInt(Key(slot, "yaw"), b.Yaw);
        view.SetInt(Key(slot, "roll"), b.Roll);
        RecompOne.Runtime.Runtime.SaveView();
    }

    internal static void LoadPersisted()
    {
        var view = RecompOne.Runtime.Runtime.View;
        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i] = new Bookmark
            {
                Set   = view.GetBool(Key(i, "set"), false),
                Area  = view.GetInt(Key(i, "area"), 0),
                X     = view.GetInt(Key(i, "x"), 0),
                Y     = view.GetInt(Key(i, "y"), 0),
                Z     = view.GetInt(Key(i, "z"), 0),
                Pitch = view.GetInt(Key(i, "pitch"), 0),
                Yaw   = view.GetInt(Key(i, "yaw"), 0),
                Roll  = view.GetInt(Key(i, "roll"), 0),
            };
        }
    }

    internal static void Clear(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return;
        _slots[slot] = default;
        Persist(slot);
        Status = $"bookmark {slot + 1} cleared";
    }
}
