using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// The flat sprites turned to the eye, handed to <c>func_8003E34C</c> as its matrix
/// argument by the C# walk (<see cref="ModelWalk"/>).
///
/// The billboard table (the flames: two quads, one facing +Z and one -Z) is drawn with
/// the view matrix and no rotation, so the game leaves it fixed in the world, edge-on
/// from east and west. The game's own upright sprites (an object with flag
/// <c>0x40</c>) are drawn with the pitch-only matrix
/// <c>0x801AEC0C</c>: their world orientation is the camera's yaw undone, so they are
/// parallel to the picture, wherever they stand in it, and stretch toward its edges.
///
///     KF3_BILLBOARDS=0 (game)   both as the game draws them
///     KF3_BILLBOARDS=1 (view)   the flames turned as the game turns its sprites
///     KF3_BILLBOARDS=2 (eye)    the flames and the sprites each turned toward the eye
///                               about the vertical (the default)
///
/// The matrix argument is only the model's rotation's left factor (the submitter
/// multiplies it into the rotation after the light matrix is taken, and places the
/// model with the view matrix), so the lighting and the position are the game's.
/// See "The flames and the sprites turned to the eye" in docs/PICTURE.md.
/// </summary>
public static class BillboardFacing
{
    public const string Key = "kf3.billboards";
    public const int Default = 2;

    const uint ViewMatrix = CameraBlock.ViewMatrix, PitchMatrix = CameraBlock.PitchMatrix;

    static int _setting = Parse(Environment.GetEnvironmentVariable("KF3_BILLBOARDS"));

    /// <summary>0 as the game, 1 the flames turned to the view, 2 everything turned to the eye.</summary>
    public static int Setting { get => _setting; set => _setting = Math.Clamp(value, 0, 2); }

    /// <summary>Submits given a turned matrix since boot: flames, then sprites.</summary>
    public static long Flames, Sprites;

    static int Parse(string? v) => v?.Trim().ToLowerInvariant() switch
    {
        "0" or "game" or "off" => 0,
        "1" or "view" => 1,
        "2" or "eye" or "on" => 2,
        _ => Default,
    };

    public enum Kind { None, Flame, Sprite }

    /// <summary>Write the turned rotation for a model at <paramref name="pos"/> (a
    /// VECTOR, the carried one if carried) to <paramref name="at"/> and return it, or
    /// return <paramref name="matrix"/>, the game's, when this kind is not turned.</summary>
    public static uint Matrix(PSMemory mem, Kind kind, uint pos, uint at, uint matrix)
    {
        if (kind == Kind.None || _setting == 0 || kind == Kind.Sprite && _setting < 2) return matrix;

        // The camera's forward on the ground: the view's third row, whose yaw the
        // pitch-only matrix undoes. View = Pitch x Yaw, Yaw's rows (fz,0,-fx), (0,1,0), (fx,0,fz).
        double fx = (short)mem.ReadU16(ViewMatrix + 12u), fz = (short)mem.ReadU16(ViewMatrix + 16u);
        double fl = Math.Sqrt(fx * fx + fz * fz);
        if (fl < 1) return matrix;
        fx /= fl; fz /= fl;

        // Where the sprite's +Z goes: the forward (the game's sprites) or from the eye to it.
        double dx = fx, dz = fz;
        if (_setting == 2)
        {
            double ex = (int)mem.ReadU32(pos) - (double)(int)mem.ReadU32(CameraBlock.Position);
            double ez = (int)mem.ReadU32(pos + 8u) - (double)(int)mem.ReadU32(CameraBlock.Position + 8u);
            double el = Math.Sqrt(ex * ex + ez * ez);
            if (el >= 1) { dx = ex / el; dz = ez / el; }
        }

        // View x Turned^T, Turned's rows (dz,0,-dx), (0,1,0), (dx,0,dz): a turn about the
        // vertical by (c, s) on a base's right. A flame's base is the view itself; a
        // sprite's is the game's pitch matrix with Yaw x Turned^T (View = Pitch x Yaw),
        // so it keeps that matrix's scale.
        uint baseAddr;
        double c, s;
        if (kind == Kind.Flame) { baseAddr = ViewMatrix; c = dz; s = dx; Flames++; }
        else { baseAddr = PitchMatrix; c = fz * dz + fx * dx; s = fz * dx - fx * dz; Sprites++; }
        for (uint i = 0; i < 3; i++)
        {
            uint row = baseAddr + i * 6u;
            double m0 = (short)mem.ReadU16(row), m2 = (short)mem.ReadU16(row + 4u);
            mem.WriteU16(at + i * 6u, (ushort)(short)Math.Round(m0 * c - m2 * s));
            mem.WriteU16(at + i * 6u + 2u, mem.ReadU16(row + 2u));
            mem.WriteU16(at + i * 6u + 4u, (ushort)(short)Math.Round(m0 * s + m2 * c));
        }
        mem.WriteU16(at + 18u, 0);
        return at;
    }
}
