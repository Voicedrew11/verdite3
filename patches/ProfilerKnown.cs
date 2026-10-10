namespace Kf3;

/// <summary>
/// The addresses this port knows, with the names the frame profiler shows for them.
/// Verdite Core's <c>FrameProfiler</c> takes this table in <c>Configure</c>; a row
/// whose label starts "stage " is one of the main-loop stages the "stages" probe times.
/// </summary>
static class ProfilerKnown
{
    public static readonly (string Overlay, uint Addr, string Label)[] Table =
    [
        ("game", 0x800341E8, "stage 1"),
        ("game", 0x80034180, "stage 2"),
        ("game", 0x80047010, "stage 3"),
        ("game", 0x80030FCC, "stage 4: the player's tick"),
        ("game", 0x80052E5C, "stage 5"),
        ("game", 0x8005BC50, "stage 6"),
        ("game", 0x8005EB20, "stage 7"),
        ("game", 0x80018358, "stage 8"),
        ("game", 0x80061940, "stage 9"),
        ("game", 0x8002B330, "stage 10: the camera"),
        ("game", 0x800156BC, "stage 11"),
        ("game", 0x80034300, "stage 12"),
        ("game", 0x80018CD0, "stage 13"),
        ("game", 0x80015A48, "stage 14"),
        ("game", 0x800422B8, "stage 15: builds and draws the frame"),
        ("game", 0x800357E8, "camera block"),
        ("game", 0x800351FC, "scrolling textures"),
        ("game", 0x80041F9C, "fade stepper, message box"),
        ("game", 0x80034BF4, "cull grid"),
        ("game", 0x80035630, "flip, clear the tables"),
        ("game", 0x80043858, "sound slots"),
        ("game", 0x8003DF50, "first-person arm"),
        ("game", 0x8003C35C, "HUD models"),
        ("game", 0x80041E68, "overlays"),
        ("game", 0x8003BFD0, "map tile walk"),
        ("game", 0x80040AE4, "model walk"),
        ("game", 0x80035700, "frame swap"),
        ("game", 0x80019614, "frame gate"),
        ("game", 0x80043940, "sound slots serviced"),
        ("game", 0x800270F8, "menu presenter"),
        ("game", 0x8007A104, "DrawOTag"),
        ("game", 0x8007910C, "VSync"),
        ("open", 0x800166CC, "DrawOTag"),
        ("open", 0x8001FE6C, "VSync"),
        ("end", 0x80014428, "DrawOTag"),
        ("end", 0x8001C208, "VSync"),
    ];
}
