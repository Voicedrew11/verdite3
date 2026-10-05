using System.Globalization;
using ImGuiNET;
using RecompOne.Runtime;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>Game settings for the existing retained lighting/normal/filter passes.</summary>
public static class SceneFeatures
{
    sealed record Switch(string Key, string Env, string Label, Func<bool> Get, Action<bool> Set);
    static readonly Switch[] Switches =
    [
        new("kf3.perpixel", "KF3_PERPIXEL", "perpixel", () => GteLightMap.Enabled, v => GteLightMap.Enabled = v),
        new("kf3.fogdepth", "KF3_FOG_DEPTH", "fog", () => RetainedScene.MainFogFromZ, v => RetainedScene.MainFogFromZ = v),
        new("kf3.ao", "KF3_AO", "ao", () => GteDepth.AmbientOcclusion, v => GteDepth.AmbientOcclusion = v),
        new("kf3.ao.normals", "KF3_AO_NORMALS", "normals", () => AoGeometry.Enabled, v => { AoGeometry.Enabled = v; GteDepth.AoNormals = v; }),
        new("kf3.mipmaps", "KF3_MIPMAPS", "mips", () => GteDepth.Mipmaps, v => GteDepth.Mipmaps = v),
        new("kf3.neighbourblend", "KF3_NEIGHBOUR_BLEND", "blend", () => NeighbourBlend.Mode != 0,
            v => NeighbourBlend.Mode = v ? NeighbourBlend.Fog | NeighbourBlend.Light : 0),
    ];
    const string Names = """
    {"strings": {
      "kf3scene.title": {"en":"World enhancements", "pt-BR":"Melhorias do mundo", "es-419":"Mejoras del mundo"},
      "kf3scene.perpixel": {"en":"Per-pixel lighting", "pt-BR":"Iluminação por pixel", "es-419":"Iluminación por píxel"},
      "kf3scene.fog": {"en":"Fog from pixel depth", "pt-BR":"Névoa pela profundidade do pixel", "es-419":"Niebla según la profundidad del píxel"},
      "kf3scene.ao": {"en":"Ambient occlusion", "pt-BR":"Oclusão ambiente", "es-419":"Oclusión ambiental"},
      "kf3scene.normals": {"en":"Geometry normals", "pt-BR":"Normais da geometria", "es-419":"Normales de la geometría"},
      "kf3scene.mips": {"en":"Mipmaps", "pt-BR":"Mipmaps", "es-419":"Mipmaps"},
      "kf3scene.blend": {"en":"Blend light across tile edges", "pt-BR":"Misturar a luz entre as bordas dos blocos", "es-419":"Mezclar la luz entre los bordes de los bloques"},
      "kf3scene.aniso": {"en":"Texture filtering", "pt-BR":"Filtragem de texturas", "es-419":"Filtrado de texturas"},
      "kf3scene.quality": {"en":"Occlusion quality", "pt-BR":"Qualidade da oclusão", "es-419":"Calidad de la oclusión"},
      "kf3scene.low": {"en":"Low", "pt-BR":"Baixa", "es-419":"Baja"},
      "kf3scene.medium": {"en":"Medium", "pt-BR":"Média", "es-419":"Media"},
      "kf3scene.high": {"en":"High", "pt-BR":"Alta", "es-419":"Alta"},
      "kf3scene.distance": {"en":"Enhancement distance (tiles)", "pt-BR":"Distância das melhorias (blocos)", "es-419":"Distancia de las mejoras (bloques)"},
      "kf3scene.everywhere": {"en":"Enhance at every distance", "pt-BR":"Melhorar em todas as distâncias", "es-419":"Mejorar a cualquier distancia"},
      "kf3scene.far": {"en":"Draw past the game's distance", "pt-BR":"Desenhar além da distância do jogo", "es-419":"Dibujar más allá de la distancia del juego"},
      "kf3scene.fartiles": {"en":"Render distance (tiles)", "pt-BR":"Distância de renderização (blocos)", "es-419":"Distancia de renderizado (bloques)"},
      "kf3scene.fadein": {"en":"Fade in at the edge of the view", "pt-BR":"Surgir gradualmente na borda da visão", "es-419":"Aparecer gradualmente en el borde de la vista"},
      "kf3scene.fadetiles": {"en":"Fade band (tiles)", "pt-BR":"Faixa de transição (blocos)", "es-419":"Franja de transición (bloques)"},
      "kf3scene.renderer": {"en":"Scene renderer", "pt-BR":"Renderizador da cena", "es-419":"Renderizador de la escena"},
      "kf3scene.reference": {"en":"Reference packets", "pt-BR":"Pacotes de referência", "es-419":"Paquetes de referencia"},
      "kf3scene.shadow": {"en":"Retain alongside reference", "pt-BR":"Reter junto à referência", "es-419":"Retener junto a la referencia"},
      "kf3scene.gpu": {"en":"Retained GPU", "pt-BR":"GPU retida", "es-419":"GPU retenida"},
      "kf3scene.native": {"en":"Native scene reference", "pt-BR":"Referência nativa da cena", "es-419":"Referencia nativa de la escena"},
      "kf3scene.recompiled": {"en":"Recompiled", "pt-BR":"Recompilado", "es-419":"Recompilado"},
      "kf3scene.csharp": {"en":"C#", "pt-BR":"C#", "es-419":"C#"},
      "kf3scene.verify": {"en":"Verify", "pt-BR":"Verificar", "es-419":"Verificar"}
    }}
    """;
    static int _quality = 1;
    static string T(string key) => Localization.T("kf3scene." + key);
    static string? Env(string key) => Environment.GetEnvironmentVariable(key);
    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Localization.Merge(Names);
            foreach (var s in Switches)
                s.Set(Env(s.Env) is { Length: > 0 } forced ? forced is not ("0" or "off")
                    : Rt.View.GetInt(s.Key, s.Label is "fog" or "normals" ? 1 : 0) != 0);
            SetQuality(Env("KF3_AO_QUALITY")?.ToLowerInvariant() switch
                { "low" => 0, "medium" => 1, "high" => 2, _ => Rt.View.GetInt("kf3.ao.quality", 1) });
            GteDepth.Anisotropy = (int)Math.Clamp(Number("KF3_ANISO", "kf3.aniso", 1), 1, 16);
            SetDistance(Number("KF3_ENHANCEDIST", "kf3.enhancedistance", 0));
            RenderDistance.SetTiles(Number("KF3_RENDERDIST", "kf3.renderdistance", 0));
            RenderDistance.SetFade(Number("KF3_RENDERDIST_FADE", "kf3.renderdistance.fade", 0));
            RetainedScene.SurfaceCheck = Env("KF3_GPU_SURFACE_PROBE") == "1";
            SettingsRegistry.Extend("display", Draw);
            SettingsRegistry.Extend("kf3testing", DrawTesting);
        });
    }
    static float Number(string env, string key, float fallback) =>
        float.TryParse(Env(env), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value : Rt.View.GetFloat(key, fallback);
    static void Save(string key, float value) { Rt.View.SetFloat(key, value); Rt.SaveView(); }
    public static void SetQuality(int quality)
    {
        _quality = Math.Clamp(quality, 0, 2);
        GteDepth.AoResolution = _quality == 2 ? 0 : _quality + 1;
        GteDepth.AoSamples = _quality == 0 ? 8 : 16;
    }
    public static void SetDistance(float tiles) => GteDepth.PlainDepth = float.IsFinite(tiles) && tiles > 0 ? Math.Clamp(tiles, 2, 16) * 2048 : 0;
    static void Draw()
    {
        ImGui.SeparatorText(T("title"));
        foreach (var s in Switches)
        {
            bool value = s.Get();
            if (ImGui.Checkbox(T(s.Label), ref value)) { s.Set(value); Rt.View.SetInt(s.Key, value ? 1 : 0); Rt.SaveView(); }
        }
        int quality = _quality;
        if (ImGui.Combo(T("quality"), ref quality, new[] { T("low"), T("medium"), T("high") }, 3))
        { SetQuality(quality); Rt.View.SetInt("kf3.ao.quality", _quality); Rt.SaveView(); }
        int taps = GteDepth.Anisotropy;
        if (ImGui.SliderInt(T("aniso"), ref taps, 1, 16)) { GteDepth.Anisotropy = taps; Save("kf3.aniso", taps); }
        bool everywhere = GteDepth.PlainDepth <= 0;
        float tiles = everywhere ? 8 : GteDepth.PlainDepth / 2048;
        if (ImGui.Checkbox(T("everywhere"), ref everywhere)) { SetDistance(everywhere ? 0 : tiles); Save("kf3.enhancedistance", everywhere ? 0 : tiles); }
        ImGui.BeginDisabled(everywhere);
        if (ImGui.SliderFloat(T("distance"), ref tiles, 2, 16, "%.1f")) { SetDistance(tiles); Save("kf3.enhancedistance", tiles); }
        ImGui.EndDisabled();
        // The retained renderer's reach and the fade at its edge (RenderDistance).
        bool far = RenderDistance.Tiles > 0;
        float reach = far ? RenderDistance.Tiles : 16;
        if (ImGui.Checkbox(T("far"), ref far)) { RenderDistance.SetTiles(far ? reach : 0); Save("kf3.renderdistance", RenderDistance.Tiles); }
        ImGui.BeginDisabled(!far);
        if (ImGui.SliderFloat(T("fartiles"), ref reach, 8, RenderDistance.MaxTiles, "%.0f"))
        { RenderDistance.SetTiles(reach); Save("kf3.renderdistance", RenderDistance.Tiles); }
        ImGui.EndDisabled();
        bool fade = RenderDistance.FadeTiles > 0;
        float band = fade ? RenderDistance.FadeTiles : 3;
        if (ImGui.Checkbox(T("fadein"), ref fade)) { RenderDistance.SetFade(fade ? band : 0); Save("kf3.renderdistance.fade", RenderDistance.FadeTiles); }
        ImGui.BeginDisabled(!fade);
        if (ImGui.SliderFloat(T("fadetiles"), ref band, 0.5f, RenderDistance.MaxFade, "%.1f"))
        { RenderDistance.SetFade(band); Save("kf3.renderdistance.fade", RenderDistance.FadeTiles); }
        ImGui.EndDisabled();
    }
    static void DrawTesting()
    {
        int mode = GpuWorld.Setting;
        if (ImGui.Combo(T("renderer"), ref mode, new[] { T("reference"), T("shadow"), T("gpu") }, 3)) GpuWorld.Setting = mode;
        mode = NativeScene.Setting;
        if (ImGui.Combo(T("native"), ref mode, new[] { T("recompiled"), T("csharp"), T("verify") }, 3)) NativeScene.Setting = mode;
    }
}
