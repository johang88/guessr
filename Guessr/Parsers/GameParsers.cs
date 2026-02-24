using System.Globalization;
using System.Text.RegularExpressions;

namespace Guessr.Parsers;

public record ParsedScore(string Game, string Number, double Score);

public static class GameParsers
{
    private static readonly HashSet<string> LowerIsBetterGames =
        ["Travle", "Connections", "Wordle", "GuessTheMovie", "GuessTheGame"];

    public static bool IsLowerBetter(string game)
        => LowerIsBetterGames.Contains(game);

    /// <summary>Normalizes a raw score to 0–100 where 100 = best possible.</summary>
    public static double NormalizeScore(string game, double rawScore) => game switch
    {
        // Lower is better: perfect Travle = -1, worst ~20 errors
        "Travle"         => Math.Clamp((20.0 - rawScore) / 21.0 * 100.0, 0, 100),
        // Lower is better: 0 mistakes = 100, 4 mistakes = 0
        "Connections"    => Math.Clamp((4.0 - rawScore) / 4.0 * 100.0, 0, 100),
        // Lower is better: 1 guess = 100, 7 (or X) = 0
        "Wordle"         => Math.Clamp((7.0 - rawScore) / 6.0 * 100.0, 0, 100),
        "GuessTheMovie"  => Math.Clamp((7.0 - rawScore) / 6.0 * 100.0, 0, 100),
        "GuessTheGame"   => Math.Clamp((7.0 - rawScore) / 6.0 * 100.0, 0, 100),
        // Higher is better
        "FoodGuessr"     => Math.Clamp(rawScore / 15000.0 * 100.0, 0, 100),
        "TimeGuessr"     => Math.Clamp(rawScore / 50000.0 * 100.0, 0, 100),
        _                => 0,
    };

    public static List<ParsedScore> ParseAll(string text)
    {
        var results = new List<ParsedScore>();
        Func<string, ParsedScore?>[] parsers =
        [
            ParseTravle,
            ParseConnections,
            ParseWordle,
            ParseGuessTheMovie,
            ParseGuessTheGame,
            ParseFoodGuessr,
            ParseTimeGuessr,
        ];

        foreach (var parser in parsers)
        {
            try
            {
                var result = parser(text);
                if (result is not null)
                    results.Add(result);
            }
            catch { }
        }

        return results;
    }

    /// <summary>Travle: score is number of errors. -1 for perfect.</summary>
    private static ParsedScore? ParseTravle(string text)
    {
        var mPerfect = Regex.Match(
            text,
            @"#travle\s+#(\d+)\s+\+?\d*\s*\(Perfect\)",
            RegexOptions.IgnoreCase);

        if (mPerfect.Success)
            return new ParsedScore("Travle", mPerfect.Groups[1].Value, -1);

        var m = Regex.Match(text, @"#travle\s+#(\d+)\s+([+-]?\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        return new ParsedScore("Travle", m.Groups[1].Value, int.Parse(m.Groups[2].Value));
    }

    /// <summary>
    /// Connections: count rows of 4 emoji that aren't all the same colour.
    /// Capped at 4 if more than 7 rows were found (game failed).
    /// </summary>
    private static ParsedScore? ParseConnections(string text)
    {
        var m = Regex.Match(text, @"Connections\s+Puzzle\s+#(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        var number = m.Groups[1].Value;

        // Only look at the section after the header, up to the next blank line
        var emojiSection = text[(m.Index + m.Length)..];
        var nextBlank = Regex.Match(emojiSection, @"\n\s*\n");
        if (nextBlank.Success)
            emojiSection = emojiSection[..nextBlank.Index];

        var colors = ExtractEmoji(emojiSection, "🟪", "🟩", "🟨", "🟦");
        if (colors.Count == 0)
            return null;

        // Group into rows of 4
        var rows = new List<List<string>>();
        for (int i = 0; i < colors.Count; i += 4)
            rows.Add(colors.Skip(i).Take(4).ToList());

        int mistakes = 0;
        foreach (var row in rows)
        {
            if (row.Count == 4 && row.Distinct().Count() != 1)
                mistakes++;
        }

        if (rows.Count > 7)
            mistakes = 4;

        return new ParsedScore("Connections", number, mistakes);
    }

    /// <summary>
    /// Wordle: number of guesses. X/6 = 7.
    /// Puzzle number may contain comma/space thousand separators.
    /// </summary>
    private static ParsedScore? ParseWordle(string text)
    {
        var m = Regex.Match(text, @"Wordle\s+([\d\s,]+?)\s+([X\d])/6", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        var number = Regex.Replace(m.Groups[1].Value, @"[\s,]", "");
        var scoreStr = m.Groups[2].Value;
        var score = string.Equals(scoreStr, "X", StringComparison.OrdinalIgnoreCase) ? 7 : int.Parse(scoreStr);

        return new ParsedScore("Wordle", number, score);
    }

    /// <summary>GuessTheMovie: position of first 🟩. 7 if not found.</summary>
    private static ParsedScore? ParseGuessTheMovie(string text)
    {
        var m = Regex.Match(text, @"#GuessTheMovie\s+#(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        // Trim leading blank lines (header and emoji grid are separated by a blank line),
        // then cut at the first blank line after the emoji content.
        var emojiSection = text[(m.Index + m.Length)..].TrimStart('\n', '\r', ' ', '\t');
        var nextBlank = Regex.Match(emojiSection, @"\n\s*\n");
        if (nextBlank.Success)
            emojiSection = emojiSection[..nextBlank.Index];

        var squares = ExtractEmoji(emojiSection, "🟥", "🟩", "⬜");

        var greenIdx = squares.IndexOf("🟩");
        var score = greenIdx >= 0 ? greenIdx + 1 : 7;

        return new ParsedScore("GuessTheMovie", m.Groups[1].Value, score);
    }

    /// <summary>GuessTheGame: position of first 🟩. 7 if not found.</summary>
    private static ParsedScore? ParseGuessTheGame(string text)
    {
        var m = Regex.Match(text, @"#GuessTheGame\s+#(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        // Trim leading blank lines (header and emoji grid are separated by a blank line),
        // then cut at the first blank line after the emoji content.
        var emojiSection = text[(m.Index + m.Length)..].TrimStart('\n', '\r', ' ', '\t');
        var nextBlank = Regex.Match(emojiSection, @"\n\s*\n");
        if (nextBlank.Success)
            emojiSection = emojiSection[..nextBlank.Index];

        var squares = ExtractEmoji(emojiSection, "🟥", "🟨", "🟩", "⬜");

        var greenIdx = squares.IndexOf("🟩");
        var score = greenIdx >= 0 ? greenIdx + 1 : 7;

        return new ParsedScore("GuessTheGame", m.Groups[1].Value, score);
    }

    /// <summary>FoodGuessr: extract total score (higher is better).</summary>
    private static ParsedScore? ParseFoodGuessr(string text)
    {
        var m = Regex.Match(text, @"I got ([\d\s,]+?) on the FoodGuessr", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        var score = int.Parse(Regex.Replace(m.Groups[1].Value, @"[\s,]", ""));
        return new ParsedScore("FoodGuessr", "", score);
    }

    /// <summary>TimeGuessr: extract numerator score (higher is better).</summary>
    private static ParsedScore? ParseTimeGuessr(string text)
    {
        var m = Regex.Match(
            text,
            @"TimeGuessr\s+#(\d+)\s+([\d\s,]+?)/([\d\s,]+)",
            RegexOptions.IgnoreCase);

        if (!m.Success)
            return null;

        var number = m.Groups[1].Value;
        var score = int.Parse(Regex.Replace(m.Groups[2].Value, @"[\s,]", ""));

        return new ParsedScore("TimeGuessr", number, score);
    }

    /// <summary>
    /// Extracts all occurrences of the given emoji strings from text,
    /// in order. Uses Rune to correctly handle surrogate pairs.
    /// </summary>
    private static List<string> ExtractEmoji(string text, params string[] emojis)
    {
        var result = new List<string>();
        var emojiSet = new HashSet<string>(emojis);
        int i = 0;

        while (i < text.Length)
        {
            if (!System.Text.Rune.TryGetRuneAt(text, i, out var rune))
            {
                i++;
                continue;
            }

            var s = rune.ToString();
            if (emojiSet.Contains(s))
                result.Add(s);

            i += rune.Utf16SequenceLength;
        }

        return result;
    }
}
