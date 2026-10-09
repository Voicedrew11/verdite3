using System.Security.Cryptography;
using RecompOne.Runtime.Cdrom;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;

namespace Kf3;

/// <summary>
/// The window icon is one of the game's own memory-card icons, read off the disc.
///
///     KF3_ICON=orb      the shipped verdite mark instead
///     KF3_ICON=off      no icon at all
///     KF3_ICON=0        a frame of the icon's three (2, the hair blown out, by default)
///     KF3_ICON_INSTALL=0   do not write the icon into the desktop's icon theme
///
/// Each of the five save slots has its own card icon ("2-1" to "2-5" in the
/// save's title), and this is the fourth's. All five sit in CD/COM/FDAT.T entry
/// 96: the five palettes back to back at +0x422F0, 0x20 apart, and the pixels at
/// +0x438E8, 0x700 an icon, sixteen rows 0x70 apart, each row holding frame 0,
/// 1 and 2 eight bytes apart. Reconstructed so, all fifteen frames are the ones
/// this port's carda.sav carries. See "The window icon" in docs/PACKAGING.md.
///
/// The entry is found through the archive's own table, and the icon's palette and
/// pixels are checked against a fingerprint before they are used, so another
/// revision of the disc keeps the shipped mark rather than wearing whatever bytes
/// sit there. Decoding, scaling and the icon theme are Verdite Core's WindowIcon.
/// </summary>
public static class CardIcon
{
    const string Archive = "CD/COM/FDAT.T";
    const int Entry = 96;

    /// <summary>The slot whose icon this is, 1-5.</summary>
    const int Slot = 4;

    const int PaletteAt = 0x422F0 + (Slot - 1) * 0x20;
    const int PixelsAt = 0x438E8 + (Slot - 1) * 0x700;
    const int RowStride = 0x70;
    const int Frames = 3;

    /// <summary>SHA-256 of the palette and the 16 rows of three frames (24 bytes a row) as SLUS-00255 has them.</summary>
    const string Fingerprint = "2966edb234b12c4e5654adf1b14e5eb74625a99880bc9058985aefcd7c261446";

    public static void Install(string? discPath)
    {
        if (WindowIcon.Frame(Frames, byDefault: 2) is not { } frame) return;

        if (!string.IsNullOrWhiteSpace(discPath) && File.Exists(discPath))
        {
            Apply(discPath, frame);
            return;
        }

        // No disc on the command line: KingsField3.exe opened on its own. The saved
        // disc is only known once the window has loaded settings.json, and a first
        // run's only once the picker answers, both inside Entry.Run, so this read an
        // empty path and the orb stayed. The first overlay loads after both, on the
        // game thread, so the icon is set on the window's own.
        bool done = false;
        Event.AddListener<OverlayLoadedEvent>(_ =>
        {
            if (done) return;
            done = true;
            var path = RecompOne.Runtime.Runtime.CdPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.Error.WriteLine("[KF3] icon: no disc path to read it from; keeping the shipped mark");
                return;
            }

            GpuJobs.Run(() => Apply(path, frame));
        });
    }

    static void Apply(string path, int frame)
    {
        try
        {
            using var disc = DiscFs.Open(path);
            if (Read(disc, out byte[] clut, out byte[] pixels) is { } why)
            {
                Console.Error.WriteLine($"[KF3] icon: {why}; keeping the shipped mark");
                return;
            }

            WindowIcon.Apply(WindowIcon.Decode(clut, pixels, RowStride, frame));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[KF3] icon: {e.Message}; keeping the shipped mark");
        }
    }

    static string? Read(DiscFs disc, out byte[] clut, out byte[] pixels)
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

        int span = PixelsAt + 15 * RowStride + Frames * WindowIcon.Side / 2;
        if ((end - start) * 2048 < span || (long)end * 2048 > size)
            return $"entry {Entry} is too short for the icon";

        var data = disc.ReadSectors(lba + start, span);
        clut = data[PaletteAt..(PaletteAt + 32)];
        pixels = data[PixelsAt..span];

        var rows = new byte[WindowIcon.Side * Frames * WindowIcon.Side / 2];
        for (int y = 0; y < WindowIcon.Side; y++)
            Array.Copy(pixels, y * RowStride, rows, y * 24, 24);

        var hash = Convert.ToHexString(SHA256.HashData([.. clut, .. rows])).ToLowerInvariant();
        return hash == Fingerprint ? null : "the icon is not where SLUS-00255 has it";
    }
}
