using Guessr.Parsers;

namespace Guessr.Tests;

public class GameParsersTests
{
    // ── Individual text snippets (minimal, single-game) ──────────────────────

    private const string WordleText = """
        Wordle 1 707 3/6

        ⬜⬜🟩🟩⬜
        ⬜⬜🟩🟩🟩
        🟩🟩🟩🟩🟩
        """;

    private const string FoodGuessrText = """
        I got 9 360 on the FoodGuessr Daily!

        🌗🌑🌑🌑 360 (Round 1)
        🌕🌕🌕🌖 4 500 (Round 2)
        🌕🌕🌕🌖 4 500 (Round 3)

        Friday, Feb 20, 2026
        Play here: https://www.foodguessr.com/
        """;

    private const string GuessTheGameText = """
        #GuessTheGame #1378

        🎮 🟥 🟥 🟥 🟥 🟥 🟥

        #ScreenshotSleuth
        https://GuessThe.Game/p/1378
        """;

    private const string GuessTheMovieText = """
        #GuessTheMovie #506

        🎥 🟥 🟥 🟥 🟥 🟩 ⬜

        #MovieMaven
        https://GuessTheMovie.Name/p/506
        """;

    private const string TimeGuessrText = """
        TimeGuessr #996 35,634/50,000
        🌎🟩🟩⬛️ 📅🟩⬛⬛
        🌎🟩🟨⬛️ 📅🟩🟩🟨
        🌎🟩⬛️⬛️ 📅🟩🟩🟩
        🌎🟨⬛️⬛️ 📅🟩⬛⬛
        🌎🟩🟩🟨 📅⬛️⬛️⬛️
        https://timeguessr.com
        """;

    private const string ConnectionsText = """
        Connections
        Puzzle #985
        🟨🟦🟪🟦
        🟩🟩🟩🟨
        🟩🟩🟨🟦
        🟩🟩🟨🟦
        """;

    // ── Full combined string (all six games pasted together) ─────────────────

    private const string CombinedText = """
        Wordle 1 707 3/6

        ⬜⬜🟩🟩⬜
        ⬜⬜🟩🟩🟩
        🟩🟩🟩🟩🟩

        I got 9 360 on the FoodGuessr Daily!

        🌗🌑🌑🌑 360 (Round 1)
        🌕🌕🌕🌖 4 500 (Round 2)
        🌕🌕🌕🌖 4 500 (Round 3)

        Friday, Feb 20, 2026
        Play here: https://www.foodguessr.com/

        #GuessTheGame #1378

        🎮 🟥 🟥 🟥 🟥 🟥 🟥

        #ScreenshotSleuth
        https://GuessThe.Game/p/1378

        #GuessTheMovie #506

        🎥 🟥 🟥 🟥 🟥 🟩 ⬜

        #MovieMaven
        https://GuessTheMovie.Name/p/506

        TimeGuessr #996 35,634/50,000
        🌎🟩🟩⬛️ 📅🟩⬛⬛
        🌎🟩🟨⬛️ 📅🟩🟩🟨
        🌎🟩⬛️⬛️ 📅🟩🟩🟩
        🌎🟨⬛️⬛️ 📅🟩⬛⬛
        🌎🟩🟩🟨 📅⬛️⬛️⬛️
        https://timeguessr.com

        Connections
        Puzzle #985
        🟨🟦🟪🟦
        🟩🟩🟩🟨
        🟩🟩🟨🟦
        🟩🟩🟨🟦
        """;

    // ── Individual tests ─────────────────────────────────────────────────────

    [Fact]
    public void Wordle_ParsesNumberWithSpaceSeparatorAndScore()
    {
        var results = GameParsers.ParseAll(WordleText);

        var result = Assert.Single(results);
        Assert.Equal("Wordle", result.Game);
        Assert.Equal("1707", result.Number); // "1 707" → spaces stripped
        Assert.Equal(3, result.Score);
    }

    [Fact]
    public void FoodGuessr_ParsesScoreWithSpaceSeparatorAndEmptyNumber()
    {
        var results = GameParsers.ParseAll(FoodGuessrText);

        var result = Assert.Single(results);
        Assert.Equal("FoodGuessr", result.Game);
        Assert.Equal("", result.Number); // FoodGuessr has no puzzle number
        Assert.Equal(9360, result.Score); // "9 360" → spaces stripped
    }

    [Fact]
    public void GuessTheGame_AllRedSquares_ReturnsSeven()
    {
        var results = GameParsers.ParseAll(GuessTheGameText);

        var result = Assert.Single(results);
        Assert.Equal("GuessTheGame", result.Game);
        Assert.Equal("1378", result.Number);
        Assert.Equal(7, result.Score); // no 🟩 found → failed
    }

    [Fact]
    public void GuessTheMovie_GreenOnFifthSquare_ReturnsFive()
    {
        var results = GameParsers.ParseAll(GuessTheMovieText);

        var result = Assert.Single(results);
        Assert.Equal("GuessTheMovie", result.Game);
        Assert.Equal("506", result.Number);
        Assert.Equal(5, result.Score); // 🟥🟥🟥🟥🟩 → green at position 5
    }

    [Fact]
    public void TimeGuessr_ParsesScoreWithCommaSeparator()
    {
        var results = GameParsers.ParseAll(TimeGuessrText);

        var result = Assert.Single(results);
        Assert.Equal("TimeGuessr", result.Game);
        Assert.Equal("996", result.Number);
        Assert.Equal(35634, result.Score); // "35,634" → commas stripped
    }

    [Fact]
    public void Connections_FourMixedRows_ReturnsFourMistakes()
    {
        var results = GameParsers.ParseAll(ConnectionsText);

        var result = Assert.Single(results);
        Assert.Equal("Connections", result.Game);
        Assert.Equal("985", result.Number);
        Assert.Equal(4, result.Score); // all 4 rows are mixed colour → 4 mistakes
    }

    // ── Combined string test ─────────────────────────────────────────────────

    [Fact]
    public void Combined_AllSixGames_ParsedCorrectly()
    {
        var results = GameParsers.ParseAll(CombinedText);

        Assert.Equal(6, results.Count);

        var wordle = Assert.Single(results, r => r.Game == "Wordle");
        Assert.Equal("1707", wordle.Number);
        Assert.Equal(3, wordle.Score);

        var foodGuessr = Assert.Single(results, r => r.Game == "FoodGuessr");
        Assert.Equal("", foodGuessr.Number);
        Assert.Equal(9360, foodGuessr.Score);

        var guessTheGame = Assert.Single(results, r => r.Game == "GuessTheGame");
        Assert.Equal("1378", guessTheGame.Number);
        Assert.Equal(7, guessTheGame.Score);

        var guessTheMovie = Assert.Single(results, r => r.Game == "GuessTheMovie");
        Assert.Equal("506", guessTheMovie.Number);
        Assert.Equal(5, guessTheMovie.Score);

        var timeGuessr = Assert.Single(results, r => r.Game == "TimeGuessr");
        Assert.Equal("996", timeGuessr.Number);
        Assert.Equal(35634, timeGuessr.Score);

        var connections = Assert.Single(results, r => r.Game == "Connections");
        Assert.Equal("985", connections.Number);
        Assert.Equal(4, connections.Score);
    }
}
