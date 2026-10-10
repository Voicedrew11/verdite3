using System.Security.Cryptography;
using RecompOne.Runtime.Cdrom;

namespace Kf3;

/// <summary>
/// Where the card icon is on this disc, for Verdite Core's CardIcon to read.
///
/// Each of the five save slots has its own card icon ("2-1" to "2-5" in the save's
/// title), and this is the fourth's. All five sit in CD/COM/FDAT.T entry 96: the
/// five palettes back to back at +0x422F0, 0x20 apart, and the pixels at +0x438E8,
/// 0x700 an icon, sixteen rows 0x70 apart, each row holding frame 0, 1 and 2 eight
/// bytes apart. Reconstructed so, all fifteen frames are the ones this port's
/// carda.sav carries. See "The window icon" in docs/PACKAGING.md.
///
/// The entry is found through the archive's own table, and the icon's palette and
/// pixels are checked against a fingerprint before they are used, so another
/// revision of the disc wears no icon rather than whatever bytes sit there.
/// </summary>
public static class CardIconSource
{
    const string Archive = "CD/COM/FDAT.T";
    const int Entry = 96;

    /// <summary>The slot whose icon this is, 1-5.</summary>
    const int Slot = 4;

    const int PaletteAt = 0x422F0 + (Slot - 1) * 0x20;
    const int PixelsAt = 0x438E8 + (Slot - 1) * 0x700;
    const int RowStride = 0x70;
    const int Frames = Verdite.Core.CardIcon.Frames;

    /// <summary>SHA-256 of the palette and the 16 rows of three frames (24 bytes a row) as SLUS-00255 has them.</summary>
    const string Fingerprint = "2966edb234b12c4e5654adf1b14e5eb74625a99880bc9058985aefcd7c261446";

    /// <summary>The pixels come back as the rows packed to 24 bytes, the stride the core decodes.</summary>
    public static string? Read(DiscFs disc, out byte[] clut, out byte[] pixels)
    {
        clut = [];
        pixels = [];

        if (!disc.Locate(Archive, out int lba, out uint size)) return $"no {Archive}";

        // u16 count, then u16 start sectors, one past the last entry's included.
        var table = disc.ReadSector(lba);
        int count = table[0] | table[1] << 8;
        if (count <= Entry) return $"{Archive} has {count} entries, not {Entry + 1}";
        int start = table[2 + Entry * 2] | table[3 + Entry * 2] << 8;
        int end = table[4 + Entry * 2] | table[5 + Entry * 2] << 8;

        int span = PixelsAt + 15 * RowStride + Frames * Verdite.Core.WindowIcon.Side / 2;
        if ((end - start) * 2048 < span || (long)end * 2048 > size)
            return $"entry {Entry} is too short for the icon";

        var data = disc.ReadSectors(lba + start, span);
        clut = data[PaletteAt..(PaletteAt + 32)];

        var rows = new byte[Verdite.Core.WindowIcon.Side * Frames * Verdite.Core.WindowIcon.Side / 2];
        for (int y = 0; y < Verdite.Core.WindowIcon.Side; y++)
            Array.Copy(data, PixelsAt + y * RowStride, rows, y * 24, 24);

        var hash = Convert.ToHexString(SHA256.HashData([.. clut, .. rows])).ToLowerInvariant();
        if (hash != Fingerprint) return "the icon is not where SLUS-00255 has it";
        pixels = rows;
        return null;
    }
}
