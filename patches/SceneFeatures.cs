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
    const string Names = """
    {"strings": {
      "kf3scene.title": {"en":"World enhancements", "pt-BR":"Melhorias do mundo", "es-419":"Mejoras del mundo"},
      "kf3scene.perpixel": {"en":"Per-pixel lighting", "pt-BR":"Iluminação por pixel", "es-419":"Iluminación por píxel"},
      "kf3scene.fog": {"en":"Fog", "pt-BR":"Névoa", "es-419":"Niebla"},
      "kf3scene.fogcorners": {"en":"At the corners (the game's)", "pt-BR":"Nos vértices (a do jogo)", "es-419":"En los vértices (la del juego)"},
      "kf3scene.fogdepth": {"en":"By pixel depth", "pt-BR":"Pela profundidade do pixel", "es-419":"Según la profundidad del píxel"},
      "kf3scene.fogdistance": {"en":"By distance from the eye", "pt-BR":"Pela distância do olho", "es-419":"Según la distancia al ojo"},
      "kf3scene.ao": {"en":"Ambient occlusion", "pt-BR":"Oclusão ambiente", "es-419":"Oclusión ambiental"},
      "kf3scene.normals": {"en":"Geometry normals", "pt-BR":"Normais da geometria", "es-419":"Normales de la geometría"},
      "kf3scene.mips": {"en":"Mipmaps", "pt-BR":"Mipmaps", "es-419":"Mipmaps"},
      "kf3scene.blend": {"en":"Blend light across tile edges", "pt-BR":"Misturar a luz entre as bordas dos blocos", "es-419":"Mezclar la luz entre los bordes de los bloques"},
      "kf3scene.planar": {"en":"Planar reflections", "pt-BR":"Reflexos planares", "es-419":"Reflejos planares"},
      "kf3scene.murk": {"en":"Murky water", "pt-BR":"Água turva", "es-419":"Agua turbia"},
      "kf3scene.waves": {"en":"Water waves", "pt-BR":"Ondas na água", "es-419":"Olas en el agua"},
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
    public static int Quality => _quality;
    static string T(string key) => Localization.T("kf3scene." + key);
    static string? Env(string key) => Environment.GetEnvironmentVariable(key);
    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Localization.Merge(Names);
            foreach (var s in PortSettings.SceneSwitches)
                s.Apply(Env(s.Envs[0]) is { Length: > 0 } forced ? (forced is not ("0" or "off") ? 1 : 0)
                    : Rt.View.GetInt(s.Key, (int)s.Default) != 0 ? 1 : 0);
            SetFog(BootFog());
            SetQuality(Env("KF3_AO_QUALITY")?.ToLowerInvariant() switch
                { "low" => 0, "medium" => 1, "high" => 2, _ => Rt.View.GetInt("kf3.ao.quality", 1) });
            GteDepth.Anisotropy = (int)Math.Clamp(Number("KF3_ANISO", "kf3.aniso", (float)PortSettings.Anisotropy.Default), 1, 16);
            SetDistance(Number("KF3_ENHANCEDIST", "kf3.enhancedistance", 0));
            RenderDistance.SetTiles(Number("KF3_RENDERDIST", "kf3.renderdistance", (float)PortSettings.RenderDist.Default));
            RenderDistance.SetFade(Number("KF3_RENDERDIST_FADE", "kf3.renderdistance.fade", (float)PortSettings.RenderFade.Default));
            RetainedScene.SurfaceCheck = Env("KF3_GPU_SURFACE_PROBE") == "1";
            SettingsRegistry.Extend("display", Draw);
            SettingsRegistry.Extend("kf3testing", DrawTesting);
        });
    }
    static float Number(string env, string key, float fallback) =>
        float.TryParse(Env(env), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value : Rt.View.GetFloat(key, fallback);
    /// <summary>The fog (PortSettings.Fog): 0 at the corners, 1 by pixel depth, 2 by
    /// distance from the eye.</summary>
    public static int Fog => GteDepth.RadialFog ? 2 : RetainedScene.MainFogFromZ ? 1 : 0;
    public static void SetFog(int mode)
    {
        mode = Math.Clamp(mode, 0, 2);
        RetainedScene.MainFogFromZ = mode >= 1;
        GteDepth.RadialFog = mode == 2;
    }
    // KF3_FOG, then the old KF3_FOG_DEPTH switch, then the kept choice, then the old
    // kept switches (kf3.radialfog, kf3.fogdepth), so a player's file keeps what it
    // chose; with none of them, distance from the eye (PortSettings.Fog.Default).
    static int BootFog() => Env("KF3_FOG")?.Trim().ToLowerInvariant() switch
    {
        "0" or "corners" => 0,
        "1" or "depth" => 1,
        "2" or "distance" or "radial" => 2,
        _ => Env("KF3_FOG_DEPTH") is { Length: > 0 } old ? (old is "0" or "off" ? 0 : 1)
            : Rt.View.GetInt("kf3.fog", Rt.View.GetInt("kf3.radialfog", 0) != 0 ? 2
                : Rt.View.GetInt("kf3.fogdepth", -1) switch { -1 => (int)PortSettings.Fog.Default, 0 => 0, _ => 1 }),
    };
    public static void SetQuality(int quality)
    {
        _quality = Math.Clamp(quality, 0, 2);
        GteDepth.AoResolution = _quality == 2 ? 0 : _quality + 1;
        GteDepth.AoSamples = _quality == 0 ? 8 : 16;
    }
    public static void SetDistance(float tiles) => GteDepth.PlainDepth = float.IsFinite(tiles) && tiles > 0 ? Math.Clamp(tiles, 2, 16) * 2048 : 0;
    // The rows are declared in PortSettings, which the game's own menu draws from
    // too. The distances are two controls each, so they keep their own drawing, and
    // their sliders are written once, when let go (docs/SETTINGS.md).
    static void Draw()
    {
        ImGui.SeparatorText(T("title"));
        foreach (var s in PortSettings.SceneSwitches) PortSettings.Draw(s);
        PortSettings.Draw(PortSettings.Fog);
        PortSettings.Draw(PortSettings.AoQuality);
        PortSettings.Draw(PortSettings.Anisotropy);
        bool everywhere = GteDepth.PlainDepth <= 0;
        float tiles = everywhere ? 8 : GteDepth.PlainDepth / 2048;
        if (ImGui.Checkbox(T("everywhere"), ref everywhere)) Keep(PortSettings.EnhanceDistance, everywhere ? 0 : tiles);
        ImGui.BeginDisabled(everywhere);
        if (ImGui.SliderFloat(T("distance"), ref tiles, 2, 16, "%.1f")) SetDistance(tiles);
        if (ImGui.IsItemDeactivatedAfterEdit()) Keep(PortSettings.EnhanceDistance, tiles);
        ImGui.EndDisabled();
        // The retained renderer's reach and the fade at its edge (RenderDistance).
        bool far = RenderDistance.Tiles > 0;
        float reach = far ? RenderDistance.Tiles : 16;
        if (ImGui.Checkbox(T("far"), ref far)) Keep(PortSettings.RenderDist, far ? reach : 0);
        ImGui.BeginDisabled(!far);
        if (ImGui.SliderFloat(T("fartiles"), ref reach, 8, RenderDistance.MaxTiles, "%.0f")) RenderDistance.SetTiles(reach);
        if (ImGui.IsItemDeactivatedAfterEdit()) Keep(PortSettings.RenderDist, reach);
        ImGui.EndDisabled();
        bool fade = RenderDistance.FadeTiles > 0;
        float band = fade ? RenderDistance.FadeTiles : 3;
        if (ImGui.Checkbox(T("fadein"), ref fade)) Keep(PortSettings.RenderFade, fade ? band : 0);
        ImGui.BeginDisabled(!fade);
        if (ImGui.SliderFloat(T("fadetiles"), ref band, 0.5f, RenderDistance.MaxFade, "%.1f")) RenderDistance.SetFade(band);
        if (ImGui.IsItemDeactivatedAfterEdit()) Keep(PortSettings.RenderFade, band);
        ImGui.EndDisabled();
    }
    static void Keep(PortSetting s, float value) { s.Apply(value); SettingsStore.Write(s, s.Live()); }
    static void DrawTesting()
    {
        int mode = GpuWorld.Setting;
        if (ImGui.Combo(T("renderer"), ref mode, new[] { T("reference"), T("shadow"), T("gpu") }, 3)) GpuWorld.Setting = mode;
        mode = NativeScene.Setting;
        if (ImGui.Combo(T("native"), ref mode, new[] { T("recompiled"), T("csharp"), T("verify") }, 3)) NativeScene.Setting = mode;
    }
}
