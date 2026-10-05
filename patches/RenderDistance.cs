using System.Globalization;
using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// The retained map drawn past the game's own draw radius, and the edge of what is
/// drawn faded in by distance rather than popping. Both are the main view's only: the
/// guest's grid is never changed, the reflections and the packet path keep the game's
/// reach, and so do the models (the model walk only submits what the grid lit).
///
/// <para>**The game's reach** (func_80034BF4, see "Render distance" in
/// docs/WIDESCREEN.md): a tile is drawn when it lies in the 25x25 window round the eye's
/// tile, inside the cone, and its whole-tile offset (i, j) from the eye's tile has
/// i^2 + j^2 under T5, the radius byte squared (cos-scaled looking up or down). The
/// edge this draws is stepped; <see cref="GameEdge"/> is the nearest an undrawn tile in
/// the cone comes to the eye over every yaw and every place in its tile, so a fade that
/// ends there never shows a tile the game did not submit, and turning moves nothing.</para>
///
/// <para>**Render distance** (<see cref="Tiles"/>): every half the game's window and
/// radius leave out, whose box meets the view frustum and whose tile comes within the
/// distance, is added to the frame's main-view gate (runtime 0089,
/// <c>RetainedScene.CurrentMainHalves</c>). Inside the game's radius its grid decides,
/// occlusion flood included. Nothing is added below the game's own edge.</para>
///
/// <para>**The fade** (<see cref="FadeTiles"/>): runtime 0089's DistanceFade, weight 0
/// at the edge (the render distance, or the game's edge with it off) and 1 a band
/// nearer, by horizontal distance, on the map and the models alike.</para>
///
///     KF3_RENDERDIST=&lt;tiles&gt;        draw out to this many tiles (0/unset: the game's)
///     KF3_RENDERDIST_FADE=&lt;tiles&gt;   fade over this band at the edge (0/unset: none)
///     KF3_RENDERDIST_PROBE=1          a line every five seconds, and the radius byte's changes
/// </summary>
public static class RenderDistance
{
    const uint Map = 0x801D4464, TablePointer = 0x801A929C;
    public const uint RadiusAddr = 0x801AEAE9, OriginX = 0x801AEC74, OriginZ = 0x801AEC78;
    // func_8003BB04's far gate: while an area load is pending with this flag set, a mesh
    // at or past half the table is skipped (docs/GAME_INTERNALS.md).
    public const uint LoadPending = 0x8018FAD4, LoadFlag = 0x8018FAEA;
    const float Tile = 2048f;
    public const int MaxTiles = 30, MaxFade = 8;

    /// <summary>The distance drawn to, in tiles; 0 is the game's own.</summary>
    public static float Tiles { get; private set; }
    /// <summary>The band faded over at the edge, in tiles; 0 is no fade.</summary>
    public static float FadeTiles { get; private set; }
    public static bool Enabled => Tiles > 0 || FadeTiles > 0;

    public static void SetTiles(float tiles) => Tiles = float.IsFinite(tiles) && tiles > 0 ? Math.Clamp(tiles, 1, MaxTiles) : 0;
    public static void SetFade(float tiles) => FadeTiles = float.IsFinite(tiles) && tiles > 0 ? Math.Clamp(tiles, 0.25f, MaxFade) : 0;

    static bool _probe;
    static long _reportAt, _frames, _added, _addedMax, _gameMiss, _overlap, _deep, _checked, _walked;
    static float _maxDepth, _edge, _gameEdge;
    static int _radius = -1, _t5;
    static readonly List<int> _mineList = new();
    static bool _checkPending;

    /// <summary>The probe; the distance and the band are SceneFeatures' (the Video tab,
    /// KF3_RENDERDIST and KF3_RENDERDIST_FADE).</summary>
    public static void Configure() => _probe = Environment.GetEnvironmentVariable("KF3_RENDERDIST_PROBE") == "1";

    /// <summary>From GpuWorld.Begin, the frame begun and before the walk: the halves
    /// past the game's reach and the frame's fade.</summary>
    public static void Frame(PSMemory m, in RetainedScene.View v)
    {
        _mineList.Clear();
        foreach (int i in _walkList) _walk[i] = false;
        _walkList.Clear();
        _checkPending = false;
        int radius = m.ReadU8(RadiusAddr);
        if (_probe && radius != _radius)
        {
            Console.WriteLine($"[KF3] renderdistance: radius byte {(_radius < 0 ? "-" : _radius.ToString())} -> {radius} " +
                              $"in {AgentBeacon.Overlay} at tile ({(int)m.ReadU32(CameraBlock.TileX)}, {(int)m.ReadU32(CameraBlock.TileZ)})");
        }
        _radius = radius;
        if (!Enabled && !_probe) return;
        int pitch = m.ReadU16(CameraBlock.Angles) & 0xFFF;
        int s5 = CullCone.StockAngle(pitch);
        int t5, angle;
        if (CullCone.FrameCount > 0) { t5 = CullCone.FrameT5; angle = CullCone.FrameAngle; }
        else
        {
            t5 = s5 < 600 ? radius * radius : (radius * radius * Cos12(s5 >> 1)) >> 12;
            angle = s5;
        }
        _t5 = t5;
        _cx = (int)m.ReadU32(CameraBlock.TileX); _cz = (int)m.ReadU32(CameraBlock.TileZ);
        _ox = (int)m.ReadU32(OriginX); _oz = (int)m.ReadU32(OriginZ);
        _checkPending = _probe;
        float game = GameEdge(t5, angle);
        _gameEdge = game; _edge = 0;
        if (!Enabled || !RetainedMap.Ready) return;
        float far = Tiles * Tile;
        bool extend = far > game;
        float edge = extend ? far : game;
        if (extend) AddHalves(m, v, t5, edge);
        float band = Math.Min(FadeTiles * Tile, edge);
        RetainedScene.SetFade(edge, band, DistanceFade.DepthLimit);
        _edge = edge;
        _frames++; _allFrames++;
    }

    /// <summary>The halves past the game's reach, out to <paramref name="edge"/> world
    /// units, whose boxes meet the frustum, into the current frame's main-view gate;
    /// how many.</summary>
    public static int AddHalves(PSMemory m, in RetainedScene.View v, int t5, float edge)
    {
        _mineList.Clear();
        var gate = RetainedScene.CurrentMainHalves;
        if (gate.IsEmpty) return 0;
        int cx = (int)m.ReadU32(CameraBlock.TileX), cz = (int)m.ReadU32(CameraBlock.TileZ);
        int ox = (int)m.ReadU32(OriginX), oz = (int)m.ReadU32(OriginZ);
        uint table = m.ReadU32(TablePointer);
        int skipFrom = (short)m.ReadU16(LoadPending) == 1 && m.ReadU8(LoadFlag) != 0
            ? (int)(m.ReadU32(table + 4) >> 1) : int.MaxValue;
        var frustum = new Frustum(v);
        int reach = (int)Math.Ceiling(edge / Tile) + 1;
        double camX = v.CamX, camZ = v.CamZ;
        float maxDepth = 0;
        for (int z = Math.Max(0, cz - reach); z <= Math.Min(79, cz + reach); z++)
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(79, cx + reach); x++)
            {
                // The game's own: in its window and inside its radius.
                int i = z - cz, j = x - cx;
                if ((uint)(x - ox) <= 24u && (uint)(z - oz) <= 24u && i * i + j * j < t5) continue;
                double nx = Math.Clamp(camX, x * Tile, x * Tile + Tile) - camX, nz = Math.Clamp(camZ, z * Tile, z * Tile + Tile) - camZ;
                if (nx * nx + nz * nz >= (double)edge * edge) continue;
                for (int upper = 0; upper < 2; upper++)
                {
                    uint half = Map + (uint)(z * 80 + x) * 10 + (uint)upper * 5;
                    int kind = m.ReadU8(half);
                    if (kind >= 240 || kind >= skipFrom) continue;
                    float y = -(m.ReadU8(half + 1) << 7);
                    float r = Math.Max(Tile / 2, RetainedMap.MeshReach[kind]);
                    float x0 = x * Tile + Tile / 2, z0 = z * Tile + Tile / 2;
                    if (!frustum.Meets(x0 - r, x0 + r, y + RetainedMap.MeshYMin[kind], y + RetainedMap.MeshYMax[kind], z0 - r, z0 + r, out float depth))
                        continue;
                    int index = (z * 80 + x) * 2 + upper;
                    gate[index] = 255;
                    _mineList.Add(index);
                    maxDepth = Math.Max(maxDepth, depth);
                }
            }
        _added += _mineList.Count; _addedMax = Math.Max(_addedMax, _mineList.Count);
        _allAdded += _mineList.Count; _allDepth = Math.Max(_allDepth, maxDepth);
        _maxDepth = Math.Max(_maxDepth, maxDepth);
        if (maxDepth >= DistanceFade.DepthLimit - DistanceFade.DepthBand) _deep++;
        return _mineList.Count;
    }

    /// <summary>The halves the last <see cref="AddHalves"/> added, as
    /// (z * 80 + x) * 2 + upper.</summary>
    public static IReadOnlyList<int> Added => _mineList;

    /// <summary>From GpuWorld.Present, the walk done: the game's halves against the
    /// reach predicted for them, and the probe's line.</summary>
    public static void AfterWalk()
    {
        if (!_probe) return;
        if (_checkPending)
        {
            // Every half the walk drew must lie in the game's predicted reach, and none
            // of those added here may be the walk's.
            _checkPending = false;
            _checked++; _walked += _walkList.Count;
            _allChecked++; _allWalked += _walkList.Count;
            foreach (int i in _mineList) if (_walk[i]) _overlap++;
            foreach (int i in _walkList) if (!InGameReach(i)) _gameMiss++;
        }
        if (Environment.TickCount64 < _reportAt) return;
        _reportAt = Environment.TickCount64 + 5000;
        double frames = Math.Max(1, _frames), checkedFrames = Math.Max(1, _checked);
        Console.WriteLine($"[KF3] renderdistance: tiles={Tiles:0.##} fade={FadeTiles:0.##} radius={_radius} T5={_t5} " +
                          $"edge={_edge / Tile:0.00} game-edge={_gameEdge / Tile:0.00} frames={_frames} " +
                          $"added/frame={_added / frames:0.0} max={_addedMax} max-depth={_maxDepth:0} deep-frames={_deep} " +
                          $"walked/frame={_walked / checkedFrames:0.0} game-outside-reach={_gameMiss} overlap={_overlap}");
        _frames = _added = _addedMax = _deep = _checked = _walked = 0; _maxDepth = 0;
    }

    // Since the start, for the shell's gpu command.
    static long _allFrames, _allAdded, _allChecked, _allWalked;
    static float _allDepth;

    /// <summary>The shell's: cumulative counts as JSON members, for a tour to difference.</summary>
    public static string Counters() => FormattableString.Invariant(
        $"\"rdTiles\":{Tiles},\"rdFade\":{FadeTiles},\"rdRadius\":{_radius},\"rdT5\":{_t5},\"rdEdge\":{_edge:0},\"rdGameEdge\":{_gameEdge:0},") +
        FormattableString.Invariant($"\"rdFrames\":{_allFrames},\"rdAdded\":{_allAdded},\"rdMaxDepth\":{_allDepth:0},\"rdChecked\":{_allChecked},") +
        FormattableString.Invariant($"\"rdWalked\":{_allWalked},\"rdOutsideReach\":{_gameMiss},\"rdOverlap\":{_overlap}");

    /// <summary>The shell's <c>renderdist &lt;tiles&gt; [fade]</c>: set both, unsaved.</summary>
    public static string Shell(string tiles, string fade)
    {
        var inv = CultureInfo.InvariantCulture;
        if (!float.TryParse(tiles, NumberStyles.Float, inv, out float t)) return "{\"ok\":false,\"cmd\":\"renderdist\",\"error\":\"tiles?\"}";
        SetTiles(t);
        if (fade.Length > 0 && float.TryParse(fade, NumberStyles.Float, inv, out float b)) SetFade(b);
        return FormattableString.Invariant($"{{\"ok\":true,\"cmd\":\"renderdist\",\"tiles\":{Tiles},\"fade\":{FadeTiles}}}");
    }

    // The halves the walk itself drew this frame (RetainedMap.Submit), and the eye's
    // tile and the window's origin it walked from, for the probe.
    static readonly bool[] _walk = new bool[RetainedScene.HalvesW * RetainedScene.HalvesH];
    static readonly List<int> _walkList = new();
    static int _cx, _cz, _ox, _oz;

    /// <summary>The probe's: a half the game's walk drew (RetainedMap.Submit).</summary>
    public static void NoteWalk(int index)
    {
        if (!_checkPending || (uint)index >= (uint)_walk.Length) return;
        if (!_walk[index]) { _walk[index] = true; _walkList.Add(index); }
    }

    static bool InGameReach(int index)
    {
        int tile = index >> 1, x = tile % 80, z = tile / 80, i = z - _cz, j = x - _cx;
        return (uint)(x - _ox) <= 24u && (uint)(z - _oz) <= 24u && i * i + j * j < _t5;
    }

    // ---- the game's edge ------------------------------------------------------------

    static readonly Dictionary<(int, int), float> _edges = new();

    /// <summary>The nearest, in world units, that a tile the game leaves out for its
    /// radius or its window but inside its cone comes to the eye: over every yaw, and
    /// every place the eye can stand in its tile. <paramref name="angle"/> is the cone's
    /// half-angle (S5), rounded up to 32 units of 4096 for the cache.</summary>
    public static float GameEdge(int t5, int angle)
    {
        angle = Math.Min(1024, (angle + 31) & ~31);
        if (_edges.TryGetValue((t5, angle), out float e)) return e;
        e = ComputeGameEdge(t5, angle, 64, 4);
        _edges[(t5, angle)] = e;
        return e;
    }

    /// <summary>func_80034BF4's classifier over a grid wider than its window (rows and
    /// columns -8..32), at <paramref name="yaws"/> yaws and <paramref name="places"/>^2
    /// places in the eye's tile; the trig is rounded from Math, not the game's table.</summary>
    public static float ComputeGameEdge(int t5, int angle, int yaws, int places)
    {
        double best = double.MaxValue;
        for (int k = 0; k < yaws; k++)
        {
            int yaw = k * 4096 / yaws;
            var cone = new Cone(yaw, angle);
            for (int pr = 0; pr < places; pr++)
                for (int pc = 0; pc < places; pc++)
                {
                    // The eye inside its tile (the window middle's cell), edges included.
                    double er = cone.MidRow + pr / (double)(places - 1), ec = cone.MidCol + pc / (double)(places - 1);
                    for (int row = -8; row <= 32; row++)
                        for (int col = -8; col <= 32; col++)
                        {
                            int i = row - cone.MidRow, j = col - cone.MidCol;
                            bool window = (uint)row <= 24u && (uint)col <= 24u;
                            if (window && i * i + j * j < t5) continue;
                            if (!cone.Inside(row, col)) continue;
                            double dr = Math.Clamp(er, row, row + 1) - er, dc = Math.Clamp(ec, col, col + 1) - ec;
                            double d = Math.Sqrt(dr * dr + dc * dc);
                            if (d < best) best = d;
                        }
                }
        }
        return (float)(best * Tile);
    }

    /// <summary>func_80034BF4's two half-planes and its window middle, transcribed in
    /// CullCone.Rebuild: row r, column c is in the cone when a0 &lt;= 0 and a1 &gt;= 0.</summary>
    public readonly struct Cone
    {
        readonly int _s6, _s1a, _s0b, _s2b, _fp, _s3f;
        public readonly int MidRow, MidCol;
        public Cone(int yaw, int s5)
        {
            int s7 = yaw + 0x400;
            int cP = Cos12(s7), sP = Sin12(s7), cL = Cos12(s7 - s5), sL = Sin12(s7 - s5), cR = Cos12(s7 + s5), sR = Sin12(s7 + s5);
            const int C7FF = 0xC7FF;
            int t2 = (C7FF - 11 * cP) >> 12, t1 = (C7FF - 11 * sP) >> 12, amp = -11 * sP;
            int t0v = (short)t1, a3v = (short)t2;
            _s6 = (unchecked((16 * sL + amp + C7FF) << 4) >> 16) - t0v;
            _s1a = (unchecked((16 * cL + (-11 * cP) + C7FF) << 4) >> 16) - a3v;
            _s0b = (unchecked((16 * sR + amp + C7FF) << 4) >> 16) - t0v;
            _s2b = (unchecked((16 * cR + (-11 * cP) + C7FF) << 4) >> 16) - a3v;
            _s3f = (-_s0b) * a3v + _s2b * t0v;
            _fp = (-_s6) * a3v + _s1a * t0v;
            MidCol = (short)(((uint)(C7FF - (cP << 3))) >> 12);
            MidRow = (short)(((uint)(C7FF - (sP << 3))) >> 12);
        }
        public bool Inside(int row, int col) =>
            col * _s6 - row * _s1a + _fp <= 0 && col * _s0b - row * _s2b + _s3f >= 0;
    }

    static int Sin12(int a) => (int)Math.Round(4096 * Math.Sin(a * Math.PI / 2048));
    static int Cos12(int a) => (int)Math.Round(4096 * Math.Cos(a * Math.PI / 2048));

    /// <summary>The main view's frustum in world space, from the frame's view: the eye's
    /// plane and the four sides of the widened screen, a few pixels wide of it.</summary>
    readonly struct Frustum
    {
        readonly RetainedScene.View _v;
        readonly float _l, _r, _t, _b;
        public Frustum(in RetainedScene.View v)
        {
            _v = v;
            int margin = RecompOne.Runtime.Hle.Display.WideMargin(320);
            const float slack = 8f;
            _l = v.Cx + margin + slack; _r = 320 - v.Cx + margin + slack;
            _t = v.Cy + slack; _b = 240 - v.Cy + slack;
        }
        /// <summary>Whether the box can show, and the deepest view depth of its corners.</summary>
        public bool Meets(float x0, float x1, float y0, float y1, float z0, float z1, out float deepest)
        {
            int outNear = 0, outL = 0, outR = 0, outT = 0, outB = 0;
            deepest = 0;
            float h = Math.Max(1f, _v.H);
            for (int c = 0; c < 8; c++)
            {
                double wx = ((c & 1) != 0 ? x1 : x0) - _v.CamX, wy = ((c & 2) != 0 ? y1 : y0) - _v.CamY, wz = ((c & 4) != 0 ? z1 : z0) - _v.CamZ;
                double vx = _v.R00 * wx + _v.R01 * wy + _v.R02 * wz + _v.Tx;
                double vy = _v.R10 * wx + _v.R11 * wy + _v.R12 * wz + _v.Ty;
                double vz = _v.R20 * wx + _v.R21 * wy + _v.R22 * wz + _v.Tz;
                deepest = Math.Max(deepest, (float)vz);
                if (vz <= 1) outNear++;
                if (h * vx < -_l * vz) outL++;
                if (h * vx > _r * vz) outR++;
                if (h * vy < -_t * vz) outT++;
                if (h * vy > _b * vz) outB++;
            }
            return outNear < 8 && outL < 8 && outR < 8 && outT < 8 && outB < 8;
        }
    }
}
