namespace Guessr.Models;

// Request bodies
public record ParseRequest
{
    public required string Username { get; init; }
    public required string Text { get; init; }
    public string? Date { get; init; }
}

public record DeleteRequest
{
    public required string Username { get; init; }
    public required string Game { get; init; }
    public required string Date { get; init; }
}

// Response shapes for /api/scores
public class ScoreEntry
{
    public string Username { get; set; } = "";
    public string Game { get; set; } = "";
    public string? GameNumber { get; set; }
    public double ScoreValue { get; set; }
    public string PlayDate { get; set; } = "";
}

// Response shapes for /api/parse saved items
public record SavedScore(string Game, string? Number, double Score, string Date);

// Response shapes for /api/history
public class HistoryEntry
{
    public string Game { get; set; } = "";
    public string? GameNumber { get; set; }
    public double ScoreValue { get; set; }
    public string PlayDate { get; set; } = "";
}

// Response shapes for /api/leaderboard
public record PlayerScore(string Date, double Score, int Rank, double NormalizedScore);

public record PlayerStanding(string Username, int Wins, int GamesPlayed, double TotalNormalizedScore, List<PlayerScore> Scores);

public record GameLeaderboard(string Game, string? Leader, double LeaderTotalScore, List<PlayerStanding> Players);

public record LeaderboardResponse(string WeekStart, string WeekEnd, List<GameLeaderboard> Leaderboard);
