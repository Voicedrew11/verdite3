namespace Kf3;

/// <summary>
/// The in-game menu's label font, as text: what <c>func_800261DC</c> draws from a
/// list record's string. Read off a VRAM dump, 2026-10-06 ("The in-game menu, its
/// lists and its font" in docs/GAME_INTERNALS.md): capitals, digits, nine marks, a
/// bullet and a middle dot. No lower case, colon, <c>%</c>, <c>+</c> or brackets.
/// </summary>
public static class MenuFont
{
    /// <summary>A record's string is 24 bytes and ends in <c>0xFF</c>.</summary>
    public const int MaxLength = 23;

    public const byte Space = 0x7F, End = 0xFF;

    const string Marks = ".,'-=/*#!";   // 0x30..0x38

    public static byte? Code(char c) => c switch
    {
        >= 'A' and <= 'Z' => (byte)(c - 'A'),
        >= '0' and <= '9' => (byte)(0x20 + c - '0'),
        ' ' => Space,
        '?' => 0x3A,
        _ when Marks.IndexOf(c) is >= 0 and var i => (byte)(0x30 + i),
        _ => null,
    };

    /// <summary>Why <paramref name="text"/> cannot be drawn, or null when it can.</summary>
    public static string? Check(string text, int room = MaxLength)
    {
        if (text.Length == 0) return "empty";
        if (text.Length > room) return $"{text.Length} characters, {room} at most";
        foreach (char c in text)
            if (Code(c) is null) return $"'{c}' is not in the font";
        return null;
    }

    /// <summary>The string as the record holds it, <c>0xFF</c> included.</summary>
    public static byte[] Encode(string text)
    {
        if (Check(text) is { } why) throw new ArgumentException($"menu text \"{text}\": {why}");
        var bytes = new byte[text.Length + 1];
        for (int i = 0; i < text.Length; i++) bytes[i] = Code(text[i])!.Value;
        bytes[^1] = End;
        return bytes;
    }
}
