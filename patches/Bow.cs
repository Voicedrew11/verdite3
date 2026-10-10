namespace Kf3;

/// <summary>
/// The bow as the core's rumble and gyro aim read it (Verdite Core's
/// <see cref="Verdite.Core.BowReads"/>). KF3 inlines the bow's branch of
/// <c>func_8002D2A0</c> rather than reading it off a routine of its own, so the reads
/// are the look routine's tick and memory (<see cref="MouseLook"/>), the three bow
/// bytes, and the two bow ids: 27 (LARGE BOW) and 28 (ELCHRIS BOW), read off the name
/// table at <c>0x8007F620</c>.
/// </summary>
internal static class Bow
{
    internal static readonly Verdite.Core.BowReads Reads = new()
    {
        Memory = () => MouseLook.Memory,
        TickMs = () => MouseLook.TickMs,
        WeaponSlot = 0x801B25AF,    // u8, the equipped weapon id, 0xFF none
        SwingClock = 0x801B25A4,    // s16, -1 idle; the draw, then the loose
        ClipByte = 0x801B25AE,      // u8, for a bow: 0 drawn, 1 loosed
        Weapons = (27, 28),
        AttackHeld = m => (m.ReadU16(0x801B265C) & m.ReadU16(0x80081870)) != 0,
        Busy = () => ItemTurn.HoldsMouse,
        GyroWanted = () => ItemTurn.Enabled && ItemTurn.UseGyro,
    };
}
