using Dapper;
using Guessr.Models;
using Guessr.Parsers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Guessr.Data;

public class ScoreRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<ScoreRepository> _logger;

    public ScoreRepository(IDbConnectionFactory factory, ILogger<ScoreRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public void InitializeDb()
    {
        _logger.LogInformation("Initialising database schema");
        using var conn = _factory.CreateConnection();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS scores (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL,
                game TEXT NOT NULL,
                game_number TEXT,
                score_value REAL NOT NULL,
                raw_text TEXT,
                play_date TEXT NOT NULL,
                created_at TEXT DEFAULT (datetime('now')),
                UNIQUE(username, game, play_date)
            )");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_scores_date ON scores(play_date)");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_scores_user ON scores(username)");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_scores_game ON scores(game)");
        _logger.LogInformation("Database schema ready");
    }

    public void CheckHealth()
    {
        using var conn = _factory.CreateConnection();
        conn.ExecuteScalar<int>("SELECT 1");
    }

    public IEnumerable<ScoreEntry> GetScores(string date)
    {
        using var conn = _factory.CreateConnection();
        return conn.Query<ScoreEntry>(
            @"SELECT username AS Username,
                     game AS Game,
                     game_number AS GameNumber,
                     score_value AS ScoreValue,
                     play_date AS PlayDate
              FROM scores
              WHERE play_date = @date
              ORDER BY game, username",
            new { date });
    }

    public IEnumerable<HistoryEntry> GetHistory(string username)
    {
        using var conn = _factory.CreateConnection();
        return conn.Query<HistoryEntry>(
            @"SELECT game AS Game,
                     game_number AS GameNumber,
                     score_value AS ScoreValue,
                     play_date AS PlayDate
              FROM scores
              WHERE username = @username
              ORDER BY play_date DESC, game",
            new { username });
    }

    public (List<SavedScore> Saved, List<string> Errors) SaveParsedScores(
        string username, string rawText, string playDate, List<ParsedScore> scores)
    {
        using var conn = _factory.CreateConnection();
        var saved = new List<SavedScore>();
        var errors = new List<string>();

        foreach (var s in scores)
        {
            try
            {
                conn.Execute(
                    @"INSERT INTO scores (username, game, game_number, score_value, raw_text, play_date)
                      VALUES (@username, @game, @gameNumber, @scoreValue, @rawText, @playDate)",
                    new { username, game = s.Game, gameNumber = s.Number, scoreValue = s.Score, rawText, playDate });
                saved.Add(new SavedScore(s.Game, s.Number, s.Score, playDate));
                _logger.LogDebug("Saved {Game} score {Score} for {Username} on {Date}", s.Game, s.Score, username, playDate);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
            {
                errors.Add($"{s.Game}: already submitted for {playDate}");
                _logger.LogDebug("Duplicate {Game} for {Username} on {Date} — skipped", s.Game, username, playDate);
            }
        }

        return (saved, errors);
    }

    public void DeleteScore(string username, string game, string date)
    {
        _logger.LogInformation("Deleting {Game} score for {Username} on {Date}", game, username, date);
        using var conn = _factory.CreateConnection();
        conn.Execute(
            "DELETE FROM scores WHERE username = @username AND game = @game AND play_date = @date",
            new { username, game, date });
    }

    public LeaderboardResponse GetLeaderboard(int weekOffset)
    {
        var today = DateTime.Now.Date;
        // Python weekday(): Monday=0 ... Sunday=6
        // C# DayOfWeek: Sunday=0, Monday=1 ... Saturday=6
        // Convert: (dayOfWeek + 6) % 7
        var pythonWeekday = ((int)today.DayOfWeek + 6) % 7;

        // Python: start_of_week = today - timedelta(days=today.weekday()) - timedelta(weeks=-week_offset)
        // timedelta(weeks=-weekOffset) with weekOffset=-1 => timedelta(weeks=1) => subtract 7 days => go back 1 week
        var weekStart = today.AddDays(-pythonWeekday).AddDays(7 * weekOffset);
        var weekEnd = weekStart.AddDays(6);

        var startStr = weekStart.ToString("yyyy-MM-dd");
        var endStr = weekEnd.ToString("yyyy-MM-dd");

        _logger.LogDebug("Leaderboard query for week {Start} to {End} (offset={Offset})", startStr, endStr, weekOffset);

        using var conn = _factory.CreateConnection();
        var rows = conn.Query<LeaderboardRow>(
            @"SELECT username AS Username,
                     game AS Game,
                     score_value AS ScoreValue,
                     play_date AS PlayDate
              FROM scores
              WHERE play_date BETWEEN @start AND @end
              ORDER BY game, play_date",
            new { start = startStr, end = endStr }).ToList();

        // Group by (game, date) -> list of (username, score)
        var gameDateEntries = rows
            .GroupBy(r => (r.Game, r.PlayDate))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => (r.Username, r.ScoreValue)).ToList());

        // game -> username -> stats
        var gameStandings = new Dictionary<string, Dictionary<string, GameUserStats>>();

        foreach (var kvp in gameDateEntries)
        {
            var (game, date) = kvp.Key;
            var entries = kvp.Value;
            bool lowerBetter = GameParsers.IsLowerBetter(game);

            var sorted = lowerBetter
                ? entries.OrderBy(e => e.ScoreValue).ToList()
                : entries.OrderByDescending(e => e.ScoreValue).ToList();

            var rankMap = new Dictionary<string, int>();
            int currentRank = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && sorted[i].ScoreValue != sorted[i - 1].ScoreValue)
                    currentRank = i + 1;
                rankMap[sorted[i].Username] = currentRank;
            }

            if (!gameStandings.TryGetValue(game, out var userDict))
            {
                userDict = new Dictionary<string, GameUserStats>();
                gameStandings[game] = userDict;
            }

            foreach (var (username, score) in entries)
            {
                int rank = rankMap[username];

                if (!userDict.TryGetValue(username, out var stats))
                {
                    stats = new GameUserStats();
                    userDict[username] = stats;
                }

                if (rank == 1) stats.Wins++;
                stats.Scores.Add((date, score, rank));
            }
        }

        var leaderboard = new List<GameLeaderboard>();

        foreach (var game in gameStandings.Keys.OrderBy(g => g))
        {
            var players = gameStandings[game]
                .Select(kvp => new PlayerStanding(
                    kvp.Key,
                    kvp.Value.Wins,
                    kvp.Value.Scores.Count,
                    kvp.Value.Scores
                        .OrderBy(s => s.Date)
                        .Select(s => new PlayerScore(s.Date, s.Score, s.Rank))
                        .ToList()))
                .OrderByDescending(p => p.Wins)
                .ThenByDescending(p => p.GamesPlayed)
                .ToList();

            leaderboard.Add(new GameLeaderboard(
                game,
                players.FirstOrDefault()?.Username,
                players.FirstOrDefault()?.Wins ?? 0,
                players));
        }

        return new LeaderboardResponse(startStr, endStr, leaderboard);
    }

    private class LeaderboardRow
    {
        public string Username { get; set; } = "";
        public string Game { get; set; } = "";
        public double ScoreValue { get; set; }
        public string PlayDate { get; set; } = "";
    }

    private class GameUserStats
    {
        public int Wins { get; set; }
        public List<(string Date, double Score, int Rank)> Scores { get; } = new();
    }
}
