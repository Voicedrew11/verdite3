using Verdite.Launcher;

// Verdite3 -- the shipped entry point.
//
// The launcher is Verdite Core's (tools/verdite-core/launcher): it settles where
// files live, asks for a disc, builds the game from it on first run and hands
// over. This is what it needs to know about this port. See docs/PACKAGING.md.

return Launcher.Run(new LauncherGame
{
    Name = "Verdite3",
    AppId = "verdite3",
    GameTitle = "King's Field II",
    Serial = "SLUS-00255",

    // King's Field (SLUS-00158) is the other US King's Field, and the series was
    // renumbered for the West: it is the Japanese King's Field II, Verdite2's
    // game. Every address in config/ is wrong for it and it would build.
    WrongDiscs = new Dictionary<string, string>
    {
        ["SLUS-00158"] =
            "This is King's Field (SLUS-00158), which is a different game; Verdite2 plays it. " +
            "The series was renumbered for the West: this port is of King's Field II " +
            "(SLUS-00255), the US release of the Japanese King's Field III.",
    },

    RecompilerConfig = "kf3.json",
    GameAssembly = "KingsField3",
    UpdateRepository = "Voicedrew11/verdite3",

    // OPEN.EXE is the title, and GAME.EXE arriving afresh is the memory card
    // screen (its start menu) before any area; an area module or END.EXE is play.
    // See "The session and the main loop" in docs/GAME_INTERNALS.md.
    PlayAfter = overlay => overlay switch
    {
        "open" or "game" => false,
        "end" => true,
        _ when overlay.StartsWith("fdat", StringComparison.Ordinal) => true,
        _ => null,
    },
}, args);
