// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;

namespace Kf3.Mods.Debug;

/// <summary>
/// The dockable panel.
///
/// A registered IPanel rather than everything living in IMod.DrawSettings,
/// because DrawSettings only draws while the Mods popup is open -- and a readout
/// you can only see with a modal over the game is not a readout. This docks
/// alongside CPU State and Memory Editor and stays up while you play.
///
/// The tab set is the KF2 reference's plus Magic, which this game has and that one
/// did not; the tabs themselves are written against what the KF3 feature files
/// actually expose, which differ from the reference's in places (no
/// SavedCount, adjusted attributes instead of POWER, area 0..27 instead of 0..7).
/// </summary>
internal sealed class DebugPanel : IPanel
{
    internal static readonly DebugPanel Instance = new();

    public string Name => "KF3 Debug";
    public string TitleKey => "panel.kf3_debug";
    public bool IsOpen { get; set; }

    int _warpX, _warpY, _warpZ;
    bool _coordsPrimed;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(400, 620), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }
        IsOpen = open;

        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null)
        {
            ImGui.TextDisabled("Not running.");
            ImGui.End();
            return;
        }

        // Max HP is zero until an area is up, which covers the title screen and
        // the attract demo. Everything here no-ops in that state anyway; saying so
        // is better than showing a panel of dead controls.
        if (!GameState.IsInGame(mem))
        {
            ImGui.TextDisabled("No area running.");
            ImGui.TextWrapped("Load a save or start a game -- the player state block is cleared "
                            + "until an area is up, so there is nothing to read or change yet.");
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("##kf3debugtabs"))
        {
            if (ImGui.BeginTabItem("Cheats"))     { DrawCheats(mem);     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Attributes")) { DrawAttributes(mem); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Items"))      { DrawItems(mem);      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Magic"))      { DrawMagic(mem);      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Warp"))       { DrawWarp(mem);       ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("State"))      { DrawState(mem);      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Keys"))       { DrawKeys();          ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    // ---- cheats ----

    void DrawCheats(IMemory mem)
    {
        bool noclip = Noclip.Enabled;
        if (ImGui.Checkbox("Noclip flight", ref noclip)) Noclip.Enabled = noclip;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Fly through walls, with the body coming along. Forward goes where "
                           + "the camera is looking, pitch included. Your own walk keys or the "
                           + "left stick move; Page Up and Page Down -- or the pad's R2 and L2 -- "
                           + "go up and down; Left Shift or R3 is fast. F3 toggles it, as does a "
                           + "DualSense's mute key. Collision is not disabled -- the position is "
                           + "written after the game's own movement, so enemies and items keep "
                           + "theirs.");

        if (Noclip.Enabled)
        {
            ImGui.Indent();

            // The one failure this mod cannot prevent, said plainly and where it
            // will be read. Flying past the loaded area's geometry leaves the
            // renderer walking an entity table of stale pointers, and it dies on
            // an unmapped read -- the neighbouring area's module simply is not in
            // RAM. Changing area is what the Warp tab is for.
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.72f, 0.25f, 1f));
            ImGui.TextWrapped("Stay inside the area you are in. Flying out past the loaded "
                            + "geometry crashes the renderer -- the next area is not in memory. "
                            + "Use the Warp tab to change area.");
            ImGui.PopStyleColor();

            long strayed = Noclip.DistanceFromEntry(mem);
            ImGui.Text($"{strayed:n0} units from where you switched it on");
            ImGui.SameLine();
            if (ImGui.Button("Go back")) Noclip.ReturnToEntry();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Back to where noclip was switched on -- the undo for a flight "
                               + "that went too far.");

            ImGui.SliderFloat("Speed", ref Noclip.Speed, 20f, 8000f, "%.0f units/s");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Units a second, spent against real elapsed time -- so the "
                               + "flight is the same speed whatever the frame rate.");
            ImGui.SliderFloat("Fast multiplier", ref Noclip.FastMultiplier, 1f, 10f, "x%.1f");

            bool invY = Noclip.InvertVertical;
            if (ImGui.Checkbox("Invert up/down", ref invY)) Noclip.InvertVertical = invY;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Which way \"up\" is on the height axis is a convention -- both "
                               + "known spawn points sit at a negative Y. Flip this if Page Up "
                               + "takes you into the floor.");

            bool cine = Noclip.Cinematic;
            if (ImGui.Checkbox("Cinematic camera", ref cine)) Noclip.Cinematic = cine;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Ease the flight and the turn instead of landing on the input: "
                               + "the velocity builds up and coasts down, and the view trails "
                               + "where you are looking. For filming a flythrough, not for "
                               + "getting somewhere. The hotkey toasts stay silent while it is on.");

            if (Noclip.Cinematic)
            {
                ImGui.Indent();
                ImGui.SliderFloat("Move smoothing", ref Noclip.MoveSmoothing, 0.05f, 2f, "%.2f s");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Seconds to about two thirds of the speed you asked for, and "
                                   + "the same again coasting back down.");
                ImGui.SliderFloat("Look smoothing", ref Noclip.LookSmoothing, 0.05f, 2f, "%.2f s");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How far the camera trails the stick or the mouse. The turn "
                                   + "never loses ground -- it arrives late, not short.");
                ImGui.Unindent();
            }

            bool invS = Noclip.InvertStrafe;
            if (ImGui.Checkbox("Invert strafe", ref invS)) Noclip.InvertStrafe = invS;

            ImGui.TextDisabled($"entered at {Noclip.Format(Noclip.EntryPosition)}");
            ImGui.Unindent();
        }

        ImGui.Separator();

        bool god = Cheats.Invincible;
        if (ImGui.Checkbox("Invincibility", ref god)) Cheats.Invincible = god;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Zeroes the damage argument to all three of the game's HP routines, "
                           + "refuses the death latch, and restores HP each frame as a backstop. "
                           + "Blocking the latch is what covers the deaths that happen at full "
                           + "HP -- bottomless pits, drowning, being crushed and falling out of "
                           + "the level. A blocked hit is a swung-and-missed hit: no HP loss, no "
                           + "stagger, no hurt flash.");

        bool mp = Cheats.InfiniteMp;
        if (ImGui.Checkbox("Infinite MP", ref mp)) Cheats.InfiniteMp = mp;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Restores MP to its maximum at the end of every player tick, so a "
                           + "cast still pays its cost on the way in and the full-restore paths "
                           + "and regen still work.");

        bool calm = Cheats.Peaceful;
        if (ImGui.Checkbox("Enemies ignore you", ref calm)) Cheats.Peaceful = calm;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Tells the creature AI's behaviour picker that you are across the "
                           + "map, so each creature picks what it does with nobody near. They "
                           + "still appear, animate, block and take damage.");

        ImGui.Separator();

        bool speed = Cheats.SpeedEnabled;
        if (ImGui.Checkbox("Speed multiplier", ref speed)) Cheats.SpeedEnabled = speed;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Scales this frame's walk speed and turn rate before the game's own "
                           + "movement reads them, so collision and animation still run. It "
                           + "composes with whatever else writes those words, noclip included.");
        if (Cheats.SpeedEnabled)
        {
            ImGui.Indent();
            ImGui.SliderFloat("Multiplier", ref Cheats.SpeedMultiplier, 0.25f, 8f, "x%.2f");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextDisabled($"hits blocked {Cheats.BlockedHits}, deaths refused {Cheats.BlockedDeaths}, "
                         + $"HP restores {Cheats.RestoredHp}, MP restores {Cheats.RestoredMp}, "
                         + $"AI picks faked {Cheats.IgnoredPicks}");
    }

    // ---- attributes ----
    //
    // Two halves, and the split is the whole point of the tab: the character is
    // plain memory and a write sticks, while the adjusted attributes and the
    // seventeen OFFENSE/DEFENSE words are a cache func_80029500 rebuilds from the
    // equipment. Attributes.cs carries the reasoning; this draws it.

    void DrawAttributes(IMemory mem)
    {
        ImGui.TextWrapped("Everything here writes the game's own state, so it is saved with your "
                        + "character and it takes effect immediately.");

        if (ImGui.Button("Full heal")) Attributes.FullHeal(mem);
        ImGui.SameLine();
        if (ImGui.Button("Cure conditions")) Attributes.CureConditions(mem);
        ImGui.SameLine();
        if (ImGui.Button("Level up")) Attributes.QueueLevelUp();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Runs the game's own level-up routine, so the level, both maxima, the "
                           + "base attributes and the next-level threshold all move by the game's "
                           + "table. EXP is topped up to the threshold first, since the routine "
                           + "returns early below it.");
        ImGui.SameLine();
        if (ImGui.Button("Recalculate")) Attributes.QueueRecalculate();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Runs func_80029500, which rebuilds the adjusted attributes and the "
                           + "seventeen ratings from your equipment. Press it after editing a "
                           + "base attribute to see the change at once.");

        if (!string.IsNullOrEmpty(Attributes.Status))
            ImGui.TextDisabled(Attributes.Status);

        ImGui.Separator();

        if (ImGui.CollapsingHeader("Character", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            DrawFields(mem, Attributes.Progress);
            ImGui.Spacing();
            DrawFields(mem, Attributes.Vitals);
            ImGui.Spacing();
            DrawFields(mem, Attributes.BaseAttributes);
            ImGui.TextWrapped("The six base attributes are the real ones -- raising one is what "
                            + "makes you stronger for good. The adjusted numbers below are these "
                            + "plus your equipment, and the game rewrites them.");
            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Condition"))
        {
            ImGui.Indent();
            ImGui.TextWrapped("The five timers behind CONDITION on the status screen, which reads "
                            + "GOOD when all of them are zero.");
            DrawFields(mem, Attributes.Conditions);
            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Combat ratings"))
        {
            ImGui.Indent();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.72f, 0.25f, 1f));
            ImGui.TextWrapped("These twenty-three words are a cache. func_80029500 zeroes and "
                            + "rebuilds them from your equipment on every equip, item use, load "
                            + "and level-up, so a number typed here lasts seconds unless you hold "
                            + "it.");
            ImGui.PopStyleColor();

            bool locked = Attributes.LockDerived;
            if (ImGui.Checkbox("Hold these values", ref locked))
            {
                // Prime from memory first, so switching the lock on is a no-op
                // rather than a jump to whatever was last typed.
                if (locked && !Attributes.LockDerived) Attributes.PrimeHold(mem);
                Attributes.LockDerived = locked;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Rewrites all twenty-three after every recompute, from a post hook "
                               + "on func_80029500 itself. Off by default -- the base attributes "
                               + "above are the honest way to change these.");

            ImGui.Spacing();
            ImGui.Text("Adjusted");
            DrawFields(mem, Attributes.Adjusted);

            ImGui.Spacing();
            ImGui.Text("Offense");
            DrawFields(mem, Attributes.Offense, "off");

            ImGui.Spacing();
            ImGui.Text("Defense");
            DrawFields(mem, Attributes.Defense, "def");

            ImGui.Unindent();
        }
    }

    /// <summary>
    /// A column of InputInts over live memory. Each box reads the game every
    /// frame unless it is being edited, so a value the game changes shows here
    /// without a refresh; an edit writes straight through.
    ///
    /// When the hold is on, an edit to one of the held words updates the hold as
    /// well -- otherwise the next recompute would put the old number back and
    /// the box would look like it had ignored the typing.
    /// </summary>
    void DrawFields(IMemory mem, Attributes.Field[] fields, string id = "")
    {
        for (int i = 0; i < fields.Length; i++)
        {
            ref readonly var f = ref fields[i];
            int value = Attributes.Read(mem, f);

            ImGui.PushID($"{id}{f.Address:X8}");
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt(f.Name, ref value))
            {
                Attributes.Write(mem, f, value);

                int held = Attributes.HeldIndex(f.Address);
                if (held >= 0) Attributes.Held[held] = Attributes.Read(mem, f);
            }
            ImGui.PopID();

            if (f.Tip.Length != 0 && ImGui.IsItemHovered()) ImGui.SetTooltip(f.Tip);
        }
    }

    // ---- items and equipment ----

    string _itemSearch = "";
    bool _itemsHeldOnly;

    void DrawItems(IMemory mem)
    {
        var groups = Items.Groups(mem);

        ImGui.TextWrapped("The inventory is one byte per item id -- the count you hold -- and the "
                        + "id is the index into the game's own name table, so every name below is "
                        + "read out of the running image rather than from a list kept here.");

        ImGui.Text($"{Items.DistinctHeld(mem)} of {Items.Count} ids held");
        ImGui.SameLine();
        if (ImGui.SmallButton("Give one of each")) Items.QueueGiveAll(mem);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("One of every named id you do not already have, through the game's "
                           + "own give routine. Queued for the end of the next player-stage.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear all")) Items.ClearAll(mem);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Zeroes all 150 counts, both arrays. This does not unequip what you "
                           + "are wearing -- use the equipment combo below for that.");

        ImGui.Separator();

        ImGui.SetNextItemWidth(180);
        ImGui.InputText("Search", ref _itemSearch, 32);
        ImGui.SameLine();
        ImGui.Checkbox("Held only", ref _itemsHeldOnly);

        if (!string.IsNullOrEmpty(Items.Status))
        {
            ImGui.Separator();
            ImGui.TextWrapped(Items.Status);
        }

        ImGui.Separator();

        string needle = _itemSearch.Trim().ToUpperInvariant();
        bool filtering = needle.Length != 0 || _itemsHeldOnly;

        if (!ImGui.BeginChild("##itemlist", new Vector2(0, 250), ImGuiChildFlags.None))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var g in groups)
        {
            // With a filter on, a group with no surviving row should not draw a
            // heading at all -- so the rows are collected before the header is,
            // rather than the header opening onto nothing. KF3's static ranges
            // span placeholder ids, so those are skipped here too.
            var shown = new List<int>();
            for (int id = g.First; id <= g.Last; id++)
            {
                if (Items.IsUnused(mem, id)) continue;
                if (_itemsHeldOnly && !Items.Has(mem, id)) continue;
                if (needle.Length != 0 && !Items.Name(mem, id).Contains(needle, StringComparison.Ordinal))
                    continue;
                shown.Add(id);
            }
            if (shown.Count == 0) continue;

            // A search has already narrowed things; making the reader open each
            // group again would be the same work twice.
            if (filtering) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            if (!ImGui.CollapsingHeader($"{g.Name}  ({g.First}-{g.Last})###grp{g.First}")) continue;

            ImGui.Indent();
            foreach (int id in shown) DrawItemRow(mem, id);
            ImGui.Unindent();
        }

        ImGui.EndChild();

        if (ImGui.CollapsingHeader("Equipment", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            DrawEquipment(mem);
            ImGui.Unindent();
        }
    }

    void DrawItemRow(IMemory mem, int id)
    {
        int held = Items.Held(mem, id);

        ImGui.PushID(id);

        if (ImGui.SmallButton("+1")) Items.QueueGive(id);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("func_8005D898 -- the game's own give, which is what a chest calls. "
                           + "It refuses at 99.");

        ImGui.SameLine();
        if (ImGui.SmallButton("-1")) Items.QueueRemove(id);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("func_8005D7F8 -- the game's own remove-one.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputInt("##count", ref held)) Items.SetHeld(mem, id, held);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The count byte itself. Zero is how the game says you do not hold it, "
                           + "so this is also the way to drop something -- or to hold more than "
                           + "the give routine's cap of 99.");

        ImGui.SameLine();
        if (Items.Has(mem, id)) ImGui.Text($"{id,3}  {Items.Name(mem, id)}");
        else ImGui.TextDisabled($"{id,3}  {Items.Name(mem, id)}");

        ImGui.PopID();
    }

    /// <summary>
    /// The eight equipment slots, each a combo of what you hold that belongs in
    /// it. Selecting one runs the game's own setter -- func_8002BDC0 for the
    /// weapon, func_8002BB84 for the armour and rings -- which ends by rebuilding
    /// the derived stats, so an equip needs no second recompute.
    /// </summary>
    void DrawEquipment(IMemory mem)
    {
        ImGui.TextWrapped("Equipping runs the game's own setter and its stat rebuild, so the "
                        + "change shows at once and is saved. The combo offers only ids you "
                        + "hold that belong in that slot.");

        foreach (var slot in Items.EquipSlots)
        {
            int equipped = Items.Equipped(mem, slot);
            string current = equipped < 0 ? "(empty)" : Items.Label(mem, equipped);

            ImGui.Text(Items.SlotName(slot));

            ImGui.SameLine();
            ImGui.SetNextItemWidth(240);
            if (ImGui.BeginCombo($"##equip{slot}", current))
            {
                var g = GroupFor(mem, slot);
                bool any = false;
                for (int id = g.First; id <= g.Last; id++)
                {
                    if (Items.IsUnused(mem, id) || !Items.Has(mem, id)) continue;
                    if (ImGui.Selectable(Items.Label(mem, id))) Items.QueueEquip(id, slot);
                    any = true;
                }
                if (!any) ImGui.TextDisabled("nothing held for this slot");
                ImGui.EndCombo();
            }
        }
    }

    /// <summary>The inventory group a slot's ids come from, so the combo can offer them.</summary>
    static Items.Group GroupFor(IMemory mem, Items.EquipSlot slot)
    {
        string want = slot switch
        {
            Items.EquipSlot.Weapon    => "Weapons",
            Items.EquipSlot.Helm      => "Helms",
            Items.EquipSlot.Armour    => "Armour",
            Items.EquipSlot.Shield    => "Shields",
            Items.EquipSlot.Gauntlets => "Gauntlets",
            Items.EquipSlot.Boots     => "Boots",
            _                         => "Accessories",   // both rings
        };

        foreach (var g in Items.Groups(mem))
            if (g.Name == want) return g;

        return new Items.Group(want, 1, 0);   // empty
    }

    // ---- magic ----

    void DrawMagic(IMemory mem)
    {
        ImGui.TextWrapped("The spell book is its own block, one 0x18-byte record per spell: byte 0 "
                        + "is 1 when known, and u16 +0x16 is the MP cost. The game's own pages "
                        + "test that byte directly, so the checkbox is the whole of learning and "
                        + "forgetting, and the pages rebuild every time they open.");

        ImGui.Text($"selected: {Magic.Selected(mem)}");

        if (ImGui.SmallButton("Learn all")) Magic.QueueLearnAll();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Queues all {Magic.Count} records through the same path the "
                           + "checkbox uses. Known ones are skipped when the queue runs.");

        if (!string.IsNullOrEmpty(Magic.Status))
            ImGui.TextDisabled(Magic.Status);

        ImGui.Separator();

        if (!ImGui.BeginChild("##spelllist", new Vector2(0, 0), ImGuiChildFlags.None))
        {
            ImGui.EndChild();
            return;
        }

        for (int i = 0; i < Magic.Count; i++)
        {
            bool known = Magic.Known(mem, i);

            ImGui.PushID(i);
            if (ImGui.Checkbox("##known", ref known))
            {
                if (known) Magic.QueueLearn(i);
                else       Magic.QueueForget(i);
            }
            ImGui.PopID();

            ImGui.SameLine();
            ImGui.Text($"{i,2}  {Magic.Cost(mem, i),3} mp  {Magic.Name(mem, i)}");
        }

        ImGui.EndChild();
    }

    // ---- warp ----

    void DrawWarp(IMemory mem)
    {
        if (!_coordsPrimed)
        {
            // Start the boxes on where you actually are, so the first edit is a
            // nudge rather than a jump to the origin.
            (_warpX, _warpY, _warpZ) = GameState.Position(mem);
            _coordsPrimed = true;
        }

        ImGui.TextWrapped("Bookmarks remember the area they were taken in. Recalling one in the "
                        + "area you are already in writes the position and angles directly; one "
                        + "from elsewhere warps there first and places you when the load lands.");
        ImGui.Separator();

        for (int i = 0; i < Warp.SlotCount; i++)
        {
            ImGui.PushID(i);

            if (ImGui.Button("Save")) Warp.Save(i);
            ImGui.SameLine();

            if (!Warp.IsSet(i)) ImGui.BeginDisabled();
            if (ImGui.Button("Go")) Warp.Restore(i);
            ImGui.SameLine();
            if (ImGui.Button("Clear")) Warp.Clear(i);
            if (!Warp.IsSet(i)) ImGui.EndDisabled();

            ImGui.SameLine();
            if (Warp.IsSet(i))
                ImGui.Text($"{i + 1}: {Warp.AreaName(Warp.SlotArea(i))} {Noclip.Format(Warp.SlotPosition(i))}");
            else
                ImGui.TextDisabled($"{i + 1}: empty");

            ImGui.PopID();
        }

        ImGui.Separator();
        ImGui.Text("Coordinates");
        ImGui.InputInt("X", ref _warpX);
        ImGui.InputInt("Y (height)", ref _warpY);
        ImGui.InputInt("Z", ref _warpZ);

        if (ImGui.Button("Go to coordinates")) Warp.Teleport(_warpX, _warpY, _warpZ);
        ImGui.SameLine();
        if (ImGui.Button("Read current")) (_warpX, _warpY, _warpZ) = GameState.Position(mem);
        ImGui.SameLine();
        if (ImGui.Button("Snap to floor")) Noclip.SnapToFloor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Puts you on the ground at your current X/Z, using the game's own "
                           + "floor query with the player's own radius and height. This is the "
                           + "way out of a flight that ended inside a wall.");

        ImGui.Separator();
        ImGui.Text($"Area (currently {Warp.AreaName(mem.ReadU8(GameState.Area))})");
        ImGui.TextWrapped("Each button queues the game's own area change (func_80017C78, the call "
                        + "every exit makes) for the next object walk: it unloads the current area "
                        + "module and reads another off the disc. Areas do not share coordinates, "
                        + "so you arrive at the X/Z you left, moved to the nearest tile with a "
                        + "floor. A bookmark recalled in another area lands exactly.");
        bool fly = Warp.FlyAfterWarp;
        if (ImGui.Checkbox("Fly after an area warp", ref fly))
        {
            Warp.FlyAfterWarp = fly;
            DebugMod.Persist(DebugMod.FlyAfterWarpKey, fly);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Turns noclip on when you arrive, so a landing over a drop is not a "
                           + "fall. Fly to a floor and turn noclip off (F3).");

        if (Warp.Busy) ImGui.BeginDisabled();
        if (ImGui.BeginChild("##arealist", new Vector2(0, 240), ImGuiChildFlags.None))
        {
            for (int area = 0; area < Warp.AreaCount; area++)
            {
                ImGui.PushID(area);
                if (ImGui.SmallButton("Warp")) Warp.ToArea(area);
                ImGui.SameLine();
                if (area == mem.ReadU8(GameState.Area)) ImGui.Text($"{Warp.AreaName(area)}  (here)");
                else ImGui.TextDisabled(Warp.AreaName(area));
                ImGui.PopID();
            }
            ImGui.EndChild();
        }
        if (Warp.Busy) ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(Warp.Status))
        {
            ImGui.Separator();
            ImGui.TextWrapped(Warp.Status);
        }
    }

    // ---- readout ----

    void DrawState(IMemory mem)
    {
        var (x, y, z) = GameState.Position(mem);
        var (pitch, yaw, roll) = GameState.Angles(mem);

        ImGui.Text("Position");
        ImGui.Indent();
        ImGui.Text($"X {x,12}   Y {y,12}   Z {z,12}");
        ImGui.Unindent();

        ImGui.Text("View");
        ImGui.Indent();
        // Degrees as well as raw: the raw value is what you compare against the
        // game's own constants, the degrees are what tells you where you face.
        ImGui.Text($"yaw   {yaw,6}  ({yaw * 360f / GameState.AngleFull,6:0.0} deg)  increases turning left");
        ImGui.Text($"pitch {pitch,6}  ({pitch * 360f / GameState.AngleFull,6:0.0} deg)  increases looking down");
        ImGui.Text($"roll  {roll,6}");
        ImGui.Unindent();

        ImGui.Separator();
        ImGui.Text("Character");
        ImGui.Indent();
        ImGui.Text($"HP  {mem.ReadU16(GameState.Hp),5} / {mem.ReadU16(GameState.MaxHp),-5}");
        ImGui.Text($"MP  {mem.ReadU16(GameState.Mp),5} / {mem.ReadU16(GameState.MaxMp),-5}");
        ImGui.Text($"LV  {mem.ReadU8(GameState.Level),5}   EXP {mem.ReadU32(GameState.Exp)}");
        ImGui.Unindent();

        ImGui.Separator();
        ImGui.Text("Engine");
        ImGui.Indent();
        byte state = mem.ReadU8(GameState.State);
        ImGui.Text($"action state  0x{state:X2}{(state == GameState.StateDead ? "  (dead)" : "")}");
        ImGui.Text($"area          {Warp.AreaName(mem.ReadU8(GameState.Area))}");
        ImGui.Text($"save slot     {mem.ReadU8(GameState.CurrentSlot)}");
        ImGui.Text($"walk speed    {mem.ReadU32(GameState.MoveSpeed)}");
        ImGui.Text($"turn rate     {mem.ReadU32(GameState.TurnRate)}");
        ImGui.Text($"pad word      0x{mem.ReadU16(GameState.Pad):X4}");
        if (state == GameState.StateDead)
            ImGui.Text($"death frame   {GameState.ReadS16(mem, GameState.DeathClock)}");
        ImGui.Unindent();

        ImGui.Separator();
        ImGui.Text("Velocities");
        ImGui.Indent();
        ImGui.Text($"forward {GameState.ReadS16(mem, GameState.FwdVel),6}   " +
                   $"strafe {GameState.ReadS16(mem, GameState.StrafeVel),6}");
        ImGui.Text($"turn    {GameState.ReadS16(mem, GameState.TurnVel),6}   " +
                   $"pitch  {GameState.ReadS16(mem, GameState.PitchVel),6}");
        ImGui.Unindent();

        ImGui.Separator();
        ImGui.Text("Inventory");
        ImGui.Indent();
        ImGui.Text($"{Items.DistinctHeld(mem),5} of {Items.Count} ids held   (the Items tab)");
        ImGui.Unindent();

        ImGui.Separator();
        ImGui.TextDisabled("The entity table and the disc's own area names are still unmapped. "
                         + "Watching this panel while doing a thing in-game is how they get found.");
    }

    // ---- keys ----

    void DrawKeys()
    {
        bool on = Hotkeys.Enabled;
        if (ImGui.Checkbox("Hotkeys enabled", ref on)) Hotkeys.Enabled = on;

        ImGui.Separator();
        if (ImGui.BeginTable("##keys", 2, ImGuiTableFlags.SizingStretchProp))
        {
            Row("F2", "toggle this panel");
            Row("F3", "toggle noclip");
            Row("F4", "toggle invincibility");
            Row("F5 / F6", "save / go to bookmark 1");
            Row("F7", "snap to floor");
            Row("F8", "back to where noclip was switched on");
            Row("V", "film mode: cinematic camera, enemies ignore you and noclip, together (no toast)");
            Row("Page Up / Page Down", "fly up / down");
            Row("Left Shift", "fly fast (hold)");
            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.TextDisabled("On a pad");
        if (ImGui.BeginTable("##padkeys", 2, ImGuiTableFlags.SizingStretchProp))
        {
            Row("Mute / Share", "toggle noclip");
            Row("Left stick", "fly forward and strafe");
            Row("R2 / L2", "fly up / down");
            Row("R3", "fly fast (hold)");
            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.TextWrapped("F1 and F11 are the host's own (top bar, fullscreen) and are left alone. "
                        + "The shipped keyboard layout binds W A S D, the arrows, Space (attack), "
                        + "F, Q, Tab, Enter and Right Shift, so the flight keys are Page Up, "
                        + "Page Down and Left Shift, none of which any layout claims; nor does V.");

        static void Row(string key, string what)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text(key);
            ImGui.TableNextColumn(); ImGui.TextDisabled(what);
        }
    }
}
