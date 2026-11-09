namespace Kf3;

/// <summary>
/// The Japanese label glyphs, copied into the free rows of the US label font's page, so
/// the US font and the Japanese one share one texture page and one palette.
///
/// The page is the label font's: tpage 0x1D, 4-bit, at VRAM words (832, 256), 64 words
/// (256 px) wide. The menus use more Japanese glyphs than the free rows hold, but no one
/// screen comes near that, so the cells are a cache: a glyph is written to a cell the
/// first time it is drawn, and a cell is reused when its glyph has not been drawn for
/// a few vblanks. The primitives already queued for a frame read VRAM when they are drawn,
/// which is why a cell used this recently is not taken.
///
/// A cell is 14 x 15 px, laid 14 px apart, eighteen across, from page row 75 in twelve
/// rows of 15. It is free when no US block of FDAT entry 96 has a pixel in it, bar the
/// font block's zeros, and no US blank character reads from it. A Japanese glyph <c>g</c> sits at page u = 14 * (g &amp; 15),
/// v = 15 * (g &gt;&gt; 4) in the Japanese font block (docs/JAPANESE.md). The game reloads
/// the font page with entry 96, which zeroes these rows; each use checks the cell's
/// pixels against VRAM and writes them back when they differ.
/// </summary>
internal sealed class JapaneseFontAtlas
{
    /// <summary>The label font's page in VRAM, in 16-bit words, and its width in words.</summary>
    public const int PageX = 832, PageY = 256, PageWords = 64;

    const int VramWidth = 1024;
    const int PageRows = 256;
    const int PageEntry = 96;            // the FDAT.T entry that holds the font page's blocks
    const int SectorBytes = 2048;
    const int BlockHeaderBytes = 16;     // x, y, w, h, each u16 (w in words), repeated once

    /// <summary>The first page row the atlas uses: v 75, code row 0x50, the first the US font leaves empty.</summary>
    const int FirstRow = 75;
    const int CellWidth = 14, CellHeight = 15;
    const int Columns = 18;
    const int CellRows = (PageRows - FirstRow) / CellHeight;   // twelve

    /// <summary>The US blanks in the atlas's rows, as the page rectangles the US routines read: label
    /// code 0x7F (func_800261DC, u = 15 * 8, v = 7 * 15) and number code 0x0A (func_8002636C, the
    /// template's u 238, v = 10 * 15). All zeros on the disc, but they draw as whatever is there.</summary>
    static readonly (int U, int V, int Width, int Height)[] UsBlanks =
    [
        (120, 105, 7, 15),
        (238, 150, 7, 14),
    ];

    /// <summary>The Japanese font's cells: 16 across, 14 px apart, 15 px rows.</summary>
    const int GlyphColumns = 16;

    /// <summary>A cell drawn within this many vblanks may still be waiting in the frame's primitives.</summary>
    const long ProtectedVblanks = 3;

    readonly Source _font;
    readonly (int X, int Y)[] _free;
    readonly int[] _glyphAt;                                  // each cell's glyph, or -1
    readonly long[] _usedAt;                                  // each cell's last draw, in vblanks
    readonly Dictionary<int, int> _cellOf = new();            // glyph to cell index

    JapaneseFontAtlas(Source font, List<(int X, int Y)> free)
    {
        _font = font;
        _free = free.ToArray();
        _glyphAt = new int[_free.Length];
        Array.Fill(_glyphAt, -1);
        _usedAt = new long[_free.Length];
        Array.Fill(_usedAt, long.MinValue);
    }

    /// <summary>How many cells the free area holds.</summary>
    public int FreeCount => _free.Length;

    /// <summary>How many times a cell was written, for the probe.</summary>
    public int Writes { get; private set; }

    /// <summary>How many times a glyph had to take a cell drawn within the protected vblanks.</summary>
    public int ForcedEvictions { get; private set; }

    /// <summary>The page u, v for a glyph at this vblank, written into VRAM if it is not there.</summary>
    public (byte U, byte V) Cell(int glyph, long vblank, ushort[] vram, Action<uint> writeGp0)
    {
        if (!_cellOf.TryGetValue(glyph, out int cell))
        {
            cell = TakeCell(vblank);
            if (_glyphAt[cell] >= 0) _cellOf.Remove(_glyphAt[cell]);
            _glyphAt[cell] = glyph;
            _cellOf[glyph] = cell;
        }

        _usedAt[cell] = vblank;
        var (x, y) = _free[cell];
        if (!Holds(vram, glyph, x, y)) Write(vram, writeGp0, glyph, x, y);
        return ((byte)x, (byte)y);
    }

    /// <summary>A never-used cell, else the cell drawn longest ago.</summary>
    int TakeCell(long vblank)
    {
        int oldest = 0;
        for (int i = 0; i < _free.Length; i++)
        {
            if (_glyphAt[i] < 0) return i;
            if (_usedAt[i] < _usedAt[oldest]) oldest = i;
        }
        if (vblank - _usedAt[oldest] <= ProtectedVblanks) ForcedEvictions++;
        return oldest;
    }

    bool Holds(ushort[] vram, int glyph, int cellX, int cellY)
    {
        for (int dy = 0; dy < CellHeight; dy++)
            for (int dx = 0; dx < CellWidth; dx++)
                if (VramNibble(vram, cellX + dx, cellY + dy) != GlyphNibble(glyph, dx, dy))
                    return false;
        return true;
    }

    /// <summary>Writes one cell with the GPU's LoadImage words (GP0 0xA0), keeping the neighbouring pixels of the words it shares.</summary>
    void Write(ushort[] vram, Action<uint> writeGp0, int glyph, int cellX, int cellY)
    {
        int firstWord = cellX / 4, lastWord = (cellX + CellWidth - 1) / 4;
        int words = lastWord - firstWord + 1;
        var region = new ushort[words * CellHeight];
        for (int dy = 0; dy < CellHeight; dy++)
            for (int w = 0; w < words; w++)
                region[dy * words + w] = ComposeWord(vram, glyph, cellX, cellY, firstWord + w, dy);

        writeGp0(0x01000000u);
        writeGp0(0xA0000000u);
        writeGp0(Pack(PageX + firstWord, PageY + cellY));
        writeGp0(Pack(words, CellHeight));
        for (int i = 0; i < region.Length; i += 2)
            writeGp0(region[i] | (i + 1 < region.Length ? (uint)region[i + 1] << 16 : 0));
        Writes++;
    }

    /// <summary>One VRAM word of a cell's row: the glyph's nibbles inside the cell, VRAM's outside it.</summary>
    ushort ComposeWord(ushort[] vram, int glyph, int cellX, int cellY, int pageWord, int dy)
    {
        ushort word = vram[(PageY + cellY + dy) * VramWidth + PageX + pageWord];
        for (int nibble = 0; nibble < 4; nibble++)
        {
            int dx = pageWord * 4 + nibble - cellX;
            if (dx < 0 || dx >= CellWidth) continue;
            int shift = nibble * 4;
            word = (ushort)(word & ~(0xF << shift) | GlyphNibble(glyph, dx, dy) << shift);
        }
        return word;
    }

    static int VramNibble(ushort[] vram, int pageX, int pageY)
    {
        ushort word = vram[(PageY + pageY) * VramWidth + PageX + pageX / 4];
        return word >> (pageX % 4 * 4) & 0xF;
    }

    int GlyphNibble(int glyph, int dx, int dy)
    {
        int nibble = _font.Nibble(CellWidth * (glyph % GlyphColumns) + dx, CellHeight * (glyph / GlyphColumns) + dy);
        return nibble < 0 ? 0 : nibble;
    }

    static uint Pack(int low, int high) => (uint)(ushort)low | (uint)(ushort)high << 16;

    /// <summary>
    /// The atlas, from entry 96 of both discs. Throws InvalidDataException when the blocks are
    /// not as measured; the caller then keeps the labels in English.
    /// </summary>
    public static JapaneseFontAtlas Plan(byte[] usFdat, byte[] jpFdat)
    {
        var usEntry = Entry(usFdat, PageEntry);
        var jpEntry = Entry(jpFdat, PageEntry);
        var usBlocks = Blocks(usEntry).Select(b => new Source(usEntry, b)).ToList();
        var jpBlocks = Blocks(jpEntry).Select(b => new Source(jpEntry, b)).ToList();
        int usFont = FontBlock(usBlocks, "US", 168);
        int jpFont = FontBlock(jpBlocks, "Japanese", PageRows);
        return new JapaneseFontAtlas(jpBlocks[jpFont], FreeCells(usBlocks, usFont));
    }

    /// <summary>The cells of the atlas grid that no US pixel uses, in reading order.</summary>
    static List<(int X, int Y)> FreeCells(List<Source> usBlocks, int fontIndex)
    {
        var free = new List<(int X, int Y)>();
        for (int row = 0; row < CellRows; row++)
            for (int column = 0; column < Columns; column++)
            {
                int x = column * CellWidth, y = FirstRow + row * CellHeight;
                if (!HoldsUsBlank(x, y) && CellIsFree(usBlocks, fontIndex, x, y))
                    free.Add((x, y));
            }
        return free;
    }

    static bool HoldsUsBlank(int x, int y) =>
        UsBlanks.Any(blank => x < blank.U + blank.Width && x + CellWidth > blank.U &&
                              y < blank.V + blank.Height && y + CellHeight > blank.V);

    /// <summary>A cell is free when no US block but the font block covers any of its pixels,
    /// and the font block holds zeros where the cell is.</summary>
    static bool CellIsFree(List<Source> blocks, int fontIndex, int x, int y)
    {
        for (int b = 0; b < blocks.Count; b++)
        {
            if (!blocks[b].Overlaps(x, y, CellWidth, CellHeight)) continue;
            for (int dy = 0; dy < CellHeight; dy++)
                for (int dx = 0; dx < CellWidth; dx++)
                {
                    int nibble = blocks[b].Nibble(x + dx, y + dy);
                    if (nibble < 0) continue;
                    if (b == fontIndex && nibble == 0) continue;
                    return false;
                }
        }
        return true;
    }

    /// <summary>The index of the label font's block, checked for its height.</summary>
    static int FontBlock(List<Source> blocks, string disc, int rows)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            var area = blocks[i].Area;
            if (area.X != PageX || area.Y != PageY || area.W != PageWords) continue;
            if (area.H != rows)
                throw new InvalidDataException($"the {disc} label font block is {area.H} rows, not {rows}");
            return i;
        }
        throw new InvalidDataException($"no {disc} label font block at ({PageX}, {PageY}) in entry {PageEntry}");
    }

    /// <summary>A block of an entry: its position in VRAM words and its size, and where its pixels start.</summary>
    readonly record struct Block(int X, int Y, int W, int H, int DataAt);

    /// <summary>A block with the entry its pixels are in.</summary>
    readonly record struct Source(byte[] Entry, Block Area)
    {
        /// <summary>Whether the block's pixels meet a rectangle, in page coordinates.</summary>
        public bool Overlaps(int x, int y, int width, int height)
        {
            int left = (Area.X - PageX) * 4, top = Area.Y - PageY;
            return x < left + Area.W * 4 && x + width > left && y < top + Area.H && y + height > top;
        }

        /// <summary>The 4-bit pixel at a page position (x in pixels, y the page row), or -1 when the block does not cover it.</summary>
        public int Nibble(int pageX, int pageY)
        {
            int localX = pageX - (Area.X - PageX) * 4;
            int localY = pageY - (Area.Y - PageY);
            if (localX < 0 || localX >= Area.W * 4 || localY < 0 || localY >= Area.H) return -1;
            int at = Area.DataAt + localY * Area.W * 2 + localX / 2;
            return Entry[at] >> (localX % 2 * 4) & 0xF;
        }
    }

    /// <summary>The bytes of entry <paramref name="index"/>: an FDAT archive's u16 count, then u16 start sectors.</summary>
    static byte[] Entry(byte[] archive, int index)
    {
        int count = U16(archive, 0);
        if (index >= count) throw new InvalidDataException($"the archive has {count} entries, not {index + 1}");
        int start = U16(archive, 2 + index * 2), end = U16(archive, 4 + index * 2);
        if (end < start || end * SectorBytes > archive.Length)
            throw new InvalidDataException($"entry {index} is outside the archive");
        return archive[(start * SectorBytes)..(end * SectorBytes)];
    }

    /// <summary>The blocks of an entry, in order, up to its 0xFFFF end. A block is a header, then
    /// w * h * 2 bytes of pixels; an empty one (w or h 0) is skipped.</summary>
    static List<Block> Blocks(byte[] entry)
    {
        var blocks = new List<Block>();
        int at = 0;
        while (at + BlockHeaderBytes <= entry.Length)
        {
            int x = U16(entry, at), y = U16(entry, at + 2), w = U16(entry, at + 4), h = U16(entry, at + 6);
            if (x == 0xFFFF && y == 0xFFFF && w == 0xFFFF && h == 0xFFFF) break;
            if (x != U16(entry, at + 8) || y != U16(entry, at + 10) || w != U16(entry, at + 12) || h != U16(entry, at + 14))
                throw new InvalidDataException($"entry 96 has no repeated header at 0x{at:X}");
            long bytes = (long)w * h * 2;
            if (at + BlockHeaderBytes + bytes > entry.Length)
                throw new InvalidDataException($"entry 96 block at 0x{at:X} runs past the entry");
            if (bytes > 0) blocks.Add(new Block(x, y, w, h, at + BlockHeaderBytes));
            at += BlockHeaderBytes + (int)bytes;
        }
        return blocks;
    }

    static int U16(byte[] bytes, int at)
    {
        if (at < 0 || at + 2 > bytes.Length) throw new InvalidDataException($"a read at 0x{at:X} is outside the file");
        return bytes[at] | bytes[at + 1] << 8;
    }
}
