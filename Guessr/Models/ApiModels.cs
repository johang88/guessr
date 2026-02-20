namespace Guessr.Models;

// Request bodies
public record ParseRequest(string? Username, string? Text);
public record DeleteRequest(string? Username, string? Game, string? Date);

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
public record PlayerScore(string Date, double Score, bool Won);

public record PlayerStanding(string Username, int Wins, int GamesPlayed, List<PlayerScore> Scores);

public record GameLeaderboard(string Game, string? Leader, int LeaderWins, List<PlayerStanding> Players);

public record LeaderboardResponse(string WeekStart, string WeekEnd, List<GameLeaderboard> Leaderboard);
