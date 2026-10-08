using System.Reflection;
using System.Text.Json;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>The game owns scene lifetime, domains and fallback decisions.</summary>
public static class GpuWorld
{
    // 0 reference, 1 retain alongside reference, 2 retained drawing (the default).
    public static int Mode { get; private set; } = 2;
    public static int Setting
    {
        get => Mode;
        set
        {
            Mode = Math.Clamp(value, 0, 2); _frame = false;
            RetainedScene.MainSerial = RetainedScene.ArmSerial = 0;
            RetainedScene.MainView = false;
            RetainedScene.DepthStageProbe = false;
            if (Mode != 0 && NativeScene.Setting == 0) NativeScene.Setting = 1;
            if (Mode == 2 && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KF3_NEARPATH")))
                NearPath.Setting = 1;
        }
    }
    static int _scene, _hud, _preview, _arm;
    static bool _attached, _frame, _surfaceProbe, _reset;
    static long _reportAt;
    public static long Frames, Submissions, Retained, OrderVertices;
    static readonly Dictionary<string, long> Reasons = new();
    static readonly ModInfo Self = new() { Id = "kf3.gpu-world", Name = "Retained world renderer", Version = "1.0" };
    public static string Domain => _preview > 0 ? "preview" : _hud > 0 ? "hud" : _arm > 0 ? "arm" : "world";
    public static bool Capture => Mode != 0 && _frame && _scene > 0 && NativeScene.Enabled && !NativeScene.Verifying;
    public static bool Drawing => Capture && Mode == 2 && Blocker == null;
    public static bool DeferPose => Capture && (Mode == 1 || Drawing) && Domain != "hud" && Domain != "preview";
    public static string? Blocker => !RetainedScene.Supported ? "backend-capability"
        : !GteDepth.Enabled || !GteDepth.ZBuffer ? "perspective-or-depth-disabled"
        : !NativeScene.Enabled ? "native-scene-disabled"
        : NearPath.Setting != 1 || !NearPath.MapEnabled || !NearPath.ModelsEnabled ? "near-depth-disabled" : null;
    public static void Install()
    {
        Mode = Environment.GetEnvironmentVariable("KF3_GPU_WORLD")?.ToLowerInvariant() switch
        { "0" or "off" or "reference" => 0, "shadow" or "capture" => 1, _ => 2 };
        _surfaceProbe = Environment.GetEnvironmentVariable("KF3_GPU_SURFACE_PROBE") == "1";
        RetainedScene.ModelMask = Environment.GetEnvironmentVariable("KF3_GPU_MODEL_MASK") != "0";
        RetainedScene.ModelMaskProbe = Environment.GetEnvironmentVariable("KF3_GPU_MASK_PROBE") == "1";
        RetainedScene.ToleranceProbe = Environment.GetEnvironmentVariable("KF3_GPU_TOLERANCE_PROBE") == "1";
        // The swap (func_80035700) links the eight-entry front table in after the main
        // table's entry 8190, the slot the retained world is drawn at: what the game put
        // there (the sky, and the objects func_8003F304 submits) is behind everything.
        RetainedScene.UnderSlots = Environment.GetEnvironmentVariable("KF3_GPU_UNDER") == "0" ? 0 : 8;
        RetainedScene.UnderProbe = Environment.GetEnvironmentVariable("KF3_GPU_UNDER_PROBE") == "1";
        // A retained model's face reaching past the eye is clipped at the near plane, not
        // placed at the GTE's saturated ends: a billboard looked at from below drew as a
        // long sheared slab.
        RetainedScene.ModelNearClip = Environment.GetEnvironmentVariable("KF3_GPU_MODEL_CLIP") != "0";
        RetainedModels.ClipProbe = Environment.GetEnvironmentVariable("KF3_GPU_CLIP_PROBE") == "1";
        // The slope term's ceiling, in game pixels; KF3_GPU_DEPTH_CAP=0 leaves it unbounded.
        RetainedScene.DepthCapPixels = float.TryParse(Environment.GetEnvironmentVariable("KF3_GPU_DEPTH_CAP"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cap) && cap >= 0f ? cap : 1f;
        // An area module lands in the CD pump of the frame swap's VSync, after the walk
        // has handed this frame's halves and models to the retained world and before
        // the swap's DrawOTag draws them: dropping the frame there drew the world with
        // nothing, a black frame at every crossing. The reset waits for the next
        // frame's table clear (Begin), and this frame draws what it was built with.
        Event.AddListener<OverlayLoadedEvent>(_ => _reset = true);
        HookAttach.OnOverlayLoad("GPU scene lifetime", () =>
        {
            SymbolRegistry.Build();
            uint[] addresses = [0x800422B8, 0x8003C35C, 0x8004290C, 0x8003DF50, 0x80035630, 0x80035700];
            var methods = addresses.Select(a => SymbolRegistry.Resolve("game", null, a)).ToArray();
            if (methods.Any(m => m == null)) return false;
            if (!_attached)
            {
                string[] before = [nameof(SceneIn), nameof(HudIn), nameof(PreviewIn), nameof(ArmIn)];
                string[] after = [nameof(SceneOut), nameof(HudOut), nameof(PreviewOut), nameof(ArmOut)];
                for (int i = 0; i < 4; i++)
                {
                    HookManager.AddPre(Self, methods[i]!, typeof(GpuWorld).GetMethod(before[i])!, order: int.MinValue + 1);
                    HookManager.AddPost(Self, methods[i]!, typeof(GpuWorld).GetMethod(after[i])!, order: int.MaxValue - 1);
                }
                HookManager.AddPost(Self, methods[4]!, typeof(GpuWorld).GetMethod(nameof(Begin))!);
                HookManager.AddPre(Self, methods[5]!, typeof(GpuWorld).GetMethod(nameof(Present))!);
                _attached = true;
            }
            HookManager.Commit(); return methods.All(HookAttach.Installed);
        });
    }
    public static void SceneIn(CpuContext c, IMemory m) => _scene++;
    public static void SceneOut(CpuContext c, IMemory m) { _scene--; _frame = false; }
    public static void HudIn(CpuContext c, IMemory m) => _hud++;
    public static void HudOut(CpuContext c, IMemory m) => _hud--;
    public static void PreviewIn(CpuContext c, IMemory m) => _preview++;
    public static void PreviewOut(CpuContext c, IMemory m) => _preview--;
    public static void ArmIn(CpuContext c, IMemory m) => _arm++;
    public static void ArmOut(CpuContext c, IMemory m) => _arm--;
    public static void Begin(CpuContext c, IMemory m)
    {
        _frame = false;
        if (_reset)
        {
            _reset = false; RetainedScene.MainSerial = 0;
            RetainedScene.MainView = RetainedScene.DepthStageProbe = false;
            RetainedScene.ClearMeshes(); RetainedMap.Invalidate();
        }
        if (Mode == 0 || _scene == 0 || NativeScene.Verifying || m is not PSMemory mem) return;
        // Which faces are water, before the map's chunks are checked.
        WaterRects.Read(mem);
        RetainedMap.Update(mem);
        // The projection is published from Gte.Rtp, which retained drawing never
        // reaches, so it kept its 320 default against the game's 200 and narrowed
        // the view until a packet frame (the Z-buffer off) projected once. The GTE's
        // own registers are the projection, for this pass and the surface passes.
        if ((ushort)Gte.ReadControl(26) is > 0 and var h)
        {
            GteDepth.ProjH = h;
            GteDepth.ProjCx = (int)Gte.ReadControl(24) / 65536f;
            GteDepth.ProjCy = (int)Gte.ReadControl(25) / 65536f;
        }
        var view = ReadView(mem);
        RetainedScene.BeginFrame(view);
        RetainedScene.MainView = Mode == 2 && Blocker == null;
        RetainedScene.DepthStageProbe = _surfaceProbe && RetainedScene.MainView;
        RetainedScene.MainSerial = RetainedScene.MainView ? RetainedScene.Serial : 0;
        if (RetainedScene.MainView) RenderDistance.Frame(mem, view);
        // The water's clock, swell and camera, the murk's vertical, the plane finder's camera.
        Waves.Frame(view);
        Murk.Frame(view);
        PlanarMirror.Frame(view);
        _frame = true; Frames++;
    }
    public static RetainedScene.View ReadView(IMemory m)
    {
        uint p = CameraBlock.ViewMatrix; var cam = Camera.Read(m);
        return new()
        {
            R00 = (short)m.ReadU16(p) / 4096f, R01 = (short)m.ReadU16(p + 2) / 4096f, R02 = (short)m.ReadU16(p + 4) / 4096f,
            R10 = (short)m.ReadU16(p + 6) / 4096f, R11 = (short)m.ReadU16(p + 8) / 4096f, R12 = (short)m.ReadU16(p + 10) / 4096f,
            R20 = (short)m.ReadU16(p + 12) / 4096f, R21 = (short)m.ReadU16(p + 14) / 4096f, R22 = (short)m.ReadU16(p + 16) / 4096f,
            CamX = cam.X, CamY = cam.Y, CamZ = cam.Z,
            Tx = (int)m.ReadU32(p + 20), Ty = (int)m.ReadU32(p + 24), Tz = (int)m.ReadU32(p + 28),
            H = GteDepth.ProjH, Cx = GteDepth.ProjCx, Cy = GteDepth.ProjCy,
        };
    }
    public static void Fallback(uint routine, uint caller, string reason)
    {
        string key = $"{AgentBeacon.Overlay}:{Domain}:{routine:X8}:{caller:X8}:{reason}";
        Reasons.TryGetValue(key, out long n); Reasons[key] = n + 1;
    }
    public static void Present(CpuContext c, IMemory m)
    {
        if (_frame) RenderDistance.AfterWalk();
        if (!_frame || Environment.TickCount64 < _reportAt) return;
        _reportAt = Environment.TickCount64 + 5000;
        if (Environment.GetEnvironmentVariable("KF3_GPU_SURFACE_PROBE") == "1")
        {
            RetainedScene.SurfaceCheck = true;
            // ahead must stay near 0: an opaque surface in front of the depth is AO's
            // box round a billboard (GlShaders.RequireTexel).
            Console.WriteLine($"[KF3] surface probe: checks={RetainedScene.SurfaceChecks} " +
                $"pixels={RetainedScene.SurfaceDepthPixels} behind={RetainedScene.SurfaceBehind} " +
                $"ahead={RetainedScene.SurfaceAhead} missing={RetainedScene.SurfaceMissing}");
        }
        Console.WriteLine($"[KF3] retained scene: mode={Mode} frames={Frames} submitted={Submissions} retained={Retained} " +
            $"meshes={RetainedAssets.MeshBuilds}/{RetainedAssets.MeshHits} rigid={RetainedAssets.RigidBuilds}/{RetainedAssets.RigidHits} " +
            $"poses={MoPose.PoseBuilds}/{MoPose.PoseHits} deferred/materialized={MoPose.Deferred}/{MoPose.Materialized} " +
            $"GPUdraws/missed={RetainedScene.MainDraws}/{RetainedScene.MainMissed} instances={RetainedScene.InstancesDrawn} " +
            $"map/blend/normal-triangles={RetainedScene.MainTriangles}/{RetainedScene.MainWaterTriangles}/{RetainedScene.MainNormalTriangles} " +
            $"mask={(RetainedScene.ModelMask ? "on" : "off")} frames/batches={RetainedScene.MaskFrames}/{RetainedScene.MaskBatches} " +
            $"samples/behind={RetainedScene.MaskSamples}/{RetainedScene.MaskBehind} " +
            $"under-world-triangles={RetainedScene.UnderTriangles} samples/shown={RetainedScene.UnderSamples}/{RetainedScene.UnderShown} " +
            $"blend={NeighbourBlend.Mode} mixed-halves fog/light={RetainedMap.FogMixed}/{RetainedMap.LightMixed} " +
            $"record/half-uploads={RetainedScene.RecordUploads}/{NeighbourBlend.Uploads} blocker={Blocker ?? "none"}");
        if (Environment.GetEnvironmentVariable("KF3_GPU_CENSUS_FILE") is { Length: > 0 } path)
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Mode, Frames, Submissions, Retained, OrderVertices, Reasons,
                Gpu = new
                {
                    RetainedScene.MainDraws, RetainedScene.MainMissed, RetainedScene.MainTriangles,
                    RetainedScene.InstancesDrawn, RetainedScene.MainModelTriangles,
                    RetainedScene.InstanceCorners, RetainedScene.PoseTexelsUploaded, RetainedScene.RecordUploads,
                    RetainedScene.BlendNoted, RetainedScene.BlendDrawn, RetainedScene.BlendNormalInstances,
                    RetainedScene.SkyDrawn, RetainedScene.SkyFacesDrawn, RetainedScene.ArmDraws, RetainedScene.ArmMissed,
                    RetainedScene.MainWaterTriangles, RetainedScene.MainNormalTriangles,
                    RetainedScene.MainModelNormalTriangles, RetainedScene.SurfaceChecks,
                    RetainedScene.SurfaceDepthPixels, RetainedScene.SurfaceBehind, RetainedScene.SurfaceAhead,
                    RetainedScene.SurfaceMissing,
                    ModelMask = new
                    {
                        On = RetainedScene.ModelMask, RetainedScene.MaskFrames, RetainedScene.MaskBatches,
                        RetainedScene.MaskSamples, RetainedScene.MaskBehind,
                    },
                    DepthStages = new
                    {
                        RetainedScene.ProbeRuns, RetainedScene.ProbeSamples, RetainedScene.ProbeDepth,
                        RetainedScene.ProbeTargetAbsent, RetainedScene.ProbeDepthAbsent,
                        RetainedScene.ProbeSurfaceAbsent, RetainedScene.ProbeSurfaceStale, RetainedScene.ProbeSurfaceIds,
                        RetainedScene.ProbeTargetFbo, RetainedScene.ProbeFrame, RetainedScene.ProbeTargetSerial,
                        RetainedScene.ProbeTargetW, RetainedScene.ProbeTargetH,
                        RetainedScene.ProbePairRuns, RetainedScene.ProbePairFboMatch, RetainedScene.ProbePairFboMismatch,
                        RetainedScene.ProbePairSerialMatch, RetainedScene.ProbePairSerialMismatch,
                    },
                    GteDepth.AoPasses, GteDepth.AoNoTarget, AoGeometry.Passes,
                    GteDepth.MipEntries, GteDepth.MipDecodes, GteDepth.MipFull, RetainedScene.MipTableUploads,
                },
                Assets = new
                {
                    RetainedAssets.MeshBuilds, RetainedAssets.MeshHits, RetainedAssets.RigidBuilds, RetainedAssets.RigidHits,
                    MoPose.PoseBuilds, MoPose.PoseHits, MoPose.Deferred, MoPose.Materialized,
                    RetainedMap.ChunkBuilds, RetainedMap.MapUpdates, RetainedMap.RecordUpdates,
                },
                Neighbour = new
                {
                    NeighbourBlend.Mode, RetainedMap.FogMixed, RetainedMap.LightMixed, NeighbourBlend.Uploads,
                },
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
