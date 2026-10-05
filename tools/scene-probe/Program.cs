using System.Reflection;
using System.Runtime.Loader;
using Kf3;
using RecompOne.Runtime;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Context;
using Game = Recompiled.KingsField3_game;

if (args.Length != 2) throw new ArgumentException("SceneProbe <game bin> <output directory>");
string bin = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(bin, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
Directory.CreateDirectory(output);
int assertions = 0;
void Check(bool result, string reason)
{
    assertions++; if (!result) throw new InvalidOperationException(reason);
}
var memory = new PSMemory();
const uint table = 0x80010000, header = table + 12, verts = table + 0x100, normals = table + 0x200, faces = table + 0x300;
memory.WriteU32(header, verts - table - 12); memory.WriteU32(header + 4, 4);
memory.WriteU32(header + 8, normals - table - 12); memory.WriteU32(header + 12, 4);
memory.WriteU32(header + 16, faces - table - 12); memory.WriteU32(header + 20, 4);
for (uint i = 0; i < 4; i++)
{
    memory.WriteU16(verts + i * 8, (ushort)(i * 123));
    memory.WriteU16(normals + i * 8 + 2, 4096);
}
uint at = faces;
foreach (byte command in new byte[] { 0x24, 0x2E, 0x34, 0x3E })
{
    int count = (command & 8) != 0 ? 4 : 3;
    bool gouraud = (command & 16) != 0;
    uint vertexBase = count == 4 ? 18u : 14u, normalBase = vertexBase - 2;
    uint step = gouraud ? 4u : 2u;
    uint bodyBytes = (vertexBase + (uint)(count - 1) * step + 2 + 3) & ~3u;
    memory.WriteU32(at, (uint)command << 24 | bodyBytes << 6); uint body = at + 4;
    memory.WriteU16(body + 6, 0x60);
    for (uint k = 0; k < count; k++)
    {
        memory.WriteU16(body + k * 4, (ushort)(k * 0x101));
        memory.WriteU16(body + vertexBase + k * step, (ushort)(k * 8));
        if (gouraud || k == 0) memory.WriteU16(body + normalBase + (gouraud ? k * step : 0), (ushort)(k * 8));
    }
    at = body + bodyBytes;
}
var mesh = RetainedAssets.Get(memory, table, header, RetainedAssets.Family.Lit, out string reason);
Check(mesh != null, reason);
Check(mesh!.Opaque == 6 && mesh.Total == 18 && mesh.Faces.Select(f => f.Corners).SequenceEqual(new[] { 3, 6, 3, 6 }), "topology or opaque/blend partition");
Check(mesh.Faces[1].Mode == 3 && mesh.Faces[3].Semi, "blend modes");
Check(ReferenceEquals(mesh, RetainedAssets.Get(memory, table, header, RetainedAssets.Family.Lit, out _)), "mesh cache missed");
int pose = RetainedAssets.StoreRigid(memory, verts, 4);
Check(pose != 0 && pose == RetainedAssets.StoreRigid(memory, verts, 4), "rigid cache missed");
memory.WriteU16(verts, 32767);
Check(pose != RetainedAssets.StoreRigid(memory, verts, 4), "vertex mutation reused stale pose");
memory.WriteU16(normals, 2048);
Check(!ReferenceEquals(mesh, RetainedAssets.Get(memory, table, header, RetainedAssets.Family.Lit, out _)), "normal mutation reused stale mesh");
memory.WriteU16(faces + 4, 200);
Check(!ReferenceEquals(mesh, RetainedAssets.Get(memory, table, header, RetainedAssets.Family.Lit, out _)), "UV mutation reused stale mesh");
for (uint j = 0; j < 64000; j++) memory.WriteU8(0x801D4464 + j, 255);
memory.WriteU32(0x801A929C, table);
memory.WriteU8(0x801D4464, 0); memory.WriteU8(0x801D4465, 0);
memory.WriteU8(0x801D4466, 0); memory.WriteU8(0x801D4468, 0);
RetainedMap.Update(memory);
Check(RetainedScene.Static.Length == 12, "bulk map GT4 skip/topology changed");
Check(RetainedScene.Static.ToArray().All(v => (v.Light & RetainedScene.LightRecord) != 0
    && (v.Flags & RetainedScene.FlagDots) == 0), "map record colour incorrectly treated as model light dots");
RetainedScene.ClearMeshes();
Check(RetainedAssets.StoreRigid(memory, verts, 4) == 1, "generation did not reset pose cache");
var fogCases = new List<float[]>();
foreach (int near in new[] { 0, 512, 1537, 8191, 32000, 32767 })
foreach (int far in new[] { 1000, 2048, 12000, 32000, 65535 })
foreach (int depth in new[] { -8, 0, 1, 511, 1536, 5000, 11999, 32000, 65535 })
{
    int literal = near >= 32000 || near == far ? 0 : Math.Clamp(unchecked(((depth >> 2) - (near >> 2)) << 14) / (far - near), 0, 7951);
    float actual = LinearDepthCue.Weight(depth, near, far);
    Check(actual == literal, $"linear cue differs: {depth}/{near}/{far}: {literal}/{actual}");
    fogCases.Add([depth, near, far, actual]);
}
File.WriteAllText(Path.Combine(output, "fog-cases.json"), System.Text.Json.JsonSerializer.Serialize(fogCases));
// The GPU pose oracle is the game's recompiled delta decoder, including the
// ScaleMatrix short wrap, rather than another copy of the shader expression.
const uint keyAt = 0x80011000, streamAt = 0x80012000;
short[] keyVertices = [32767, -32768, 0, 0, -32768, 32767, 30000, 0, 123, -456, -30000, 0];
ushort[] targets = [32766, 32767, 32767, 32767, 32768, 35536, 30000, 30000, 30000];
for (uint k = 0; k < keyVertices.Length; k++) memory.WriteU16(keyAt + k * 2, (ushort)keyVertices[k]);
memory.WriteU16(streamAt, 3);
for (uint k = 0; k < targets.Length; k++) memory.WriteU16(streamAt + 2 + k * 2, targets[k]);
var poseCases = new List<object>();
foreach (uint weight in new uint[] { 0, 1, 10, 511, 1023, 2048, 4095, 4096, 8192 })
{
    for (uint k = 0; k < keyVertices.Length; k++) memory.WriteU16(MoPose.Posed + k * 2, (ushort)keyVertices[k]);
    var cpu = new CpuContext { SP = 0x801F8000, A0 = MoPose.Posed, A1 = streamAt, A2 = weight };
    Game.func_80042EB0(cpu, memory);
    short[] expected = Enumerable.Range(0, keyVertices.Length).Select(k => (short)memory.ReadU16(MoPose.Posed + (uint)k * 2)).ToArray();
    void Pending()
    {
        foreach (var pair in new[] { ("_key", keyAt), ("_stream", streamAt), ("_weight", weight), ("_count", 3u) })
            typeof(MoPose).GetField(pair.Item1, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, pair.Item2);
        typeof(MoPose).GetProperty(nameof(MoPose.Pending))!.SetValue(null, true);
    }
    Pending(); int stored = MoPose.Store(memory, 3, out int storedWeight);
    Check(stored != 0 && storedWeight == weight, "retained pose refused edge fixture");
    var texels = RetainedScene.PoseStore.AsSpan((stored - 1) * 4, 24).ToArray();
    poseCases.Add(new { weight, texels, expected });
    MoPose.Materialize(memory);
    Check(expected.SequenceEqual(Enumerable.Range(0, keyVertices.Length).Select(k => (short)memory.ReadU16(MoPose.Posed + (uint)k * 2))), "fallback pose differs from recompiled decoder");
}
File.WriteAllText(Path.Combine(output, "pose-cases.json"), System.Text.Json.JsonSerializer.Serialize(poseCases));
var lightCases = new List<object>();
var random = new Random(3178);
for (int sample = 0; sample < 48; sample++)
{
    var record = new int[52];
    for (int j = 0; j < 36; j++) record[j] = random.Next(-4096, 4097);
    for (int j = 36; j < 45; j++) record[j] = random.Next(-4096, 8193);
    for (int j = 45; j < 48; j++) record[j] = random.Next(0, 256) << 4;
    record[50] = 12000; record[51] = 5;
    int turn = sample % 4;
    for (int j = 0; j < 4; j++)
    {
        Gte.WriteControl(8 + j, (uint)(ushort)record[turn * 9 + j * 2] | (uint)(ushort)record[turn * 9 + j * 2 + 1] << 16);
        Gte.WriteControl(16 + j, (uint)(ushort)record[36 + j * 2] | (uint)(ushort)record[37 + j * 2] << 16);
    }
    Gte.WriteControl(12, (uint)(ushort)record[turn * 9 + 8]);
    Gte.WriteControl(20, (uint)(ushort)record[44]);
    for (int j = 0; j < 3; j++) Gte.WriteControl(13 + j, (uint)record[45 + j]);
    int[] normal = [random.Next(-4096, 4097), random.Next(-4096, 4097), random.Next(-4096, 4097)];
    uint rgbc = (uint)random.Next(0, 0x1000000);
    Gte.Write(0, (uint)(ushort)normal[0] | (uint)(ushort)normal[1] << 16);
    Gte.Write(1, (uint)(ushort)normal[2]); Gte.Write(6, rgbc); Gte.NccsOp(12, true);
    uint colour = Gte.Read(22);
    int[] expected = [(int)(colour & 255), (int)(colour >> 8 & 255), (int)(colour >> 16 & 255)];
    lightCases.Add(new { record, normal, turn, rgbc, expected });
}
File.WriteAllText(Path.Combine(output, "light-cases.json"), System.Text.Json.JsonSerializer.Serialize(lightCases));
Type shader = typeof(RetainedScene).Assembly.GetType("RecompOne.Runtime.Hle.GlShaders")!;
foreach (string name in new[] { "WorldVs", "WorldNormalVs", "PrimFs", "NormalFs", "ModelGlsl" })
{
    var field = shader.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
    File.WriteAllText(Path.Combine(output, name + ".glsl"), (string)field.GetValue(null)!);
}
File.WriteAllText(Path.Combine(output, "LinearDepthCue.glsl"), LinearDepthCue.Glsl);
SceneProbe.NativeInnerFixtures.Run(Check, output);
assertions += NearDescriptorFixtures.Run(memory, output);
SceneProbe.BulkMapFixtures.Run(Check, output);
Console.WriteLine($"Scene source probes: {assertions} assertions passed; composed shaders, {fogCases.Count} cue and {poseCases.Count} literal pose fixtures exported");
