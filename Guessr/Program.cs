using System.Text.Json;
using Guessr.Data;
using Guessr.Models;
using Guessr.Parsers;

var builder = WebApplication.CreateBuilder(args);

// Configuration from environment (same defaults as the Python app)
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "guessr_scores.db";
var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";

// Dependency injection
builder.Services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(dbPath));
builder.Services.AddScoped<ScoreRepository>();
builder.Services.AddProblemDetails();

// Use snake_case JSON to match the Python API surface exactly
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();
var log = app.Logger;

log.LogInformation("Starting Guessr {Version}, db={DbPath}", appVersion, dbPath);

// Initialise the database schema on startup
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ScoreRepository>().InitializeDb();
}

app.MapGet("/", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "wwwroot/", "index.html");
    return Results.File(path, "text/html");
});

app.MapGet("/health", (ScoreRepository repo) =>
{
    try
    {
        repo.CheckHealth();
        return Results.Json(new { status = "ok", db = "ok" });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Health check failed");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.MapGet("/api/version", () => Results.Ok(new { version = appVersion }));

app.MapPost("/api/parse", (ParseRequest data, ScoreRepository repo) =>
{
    var username = data.Username.Trim();
    var text = data.Text;

    var playDate = (data.Date is { } d && DateTime.TryParse(d, out _) ? d : null)
        ?? DateTime.Now.ToString("yyyy-MM-dd");

    if (string.Compare(playDate, DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.Ordinal) > 0)
    {
        log.LogWarning("Rejected future-date submission from {Username} for {Date}", username, playDate);
        return Results.Problem(detail: $"Cannot submit scores for a future date ({playDate})", statusCode: 400);
    }

    log.LogInformation("Parsing submission from {Username} for {Date}", username, playDate);

    var parsed = GameParsers.ParseAll(text);

    if (parsed.Count == 0)
    {
        log.LogWarning("No games parsed from {Username} submission for {Date}", username, playDate);
        return Results.Problem(detail: "Could not parse any game scores from the text", statusCode: 400);
    }

    var (saved, errors) = repo.SaveParsedScores(username.ToLower(), text, playDate, parsed);

    log.LogInformation(
        "Submission from {Username} for {Date}: {Saved} saved, {Errors} duplicate(s)",
        username, playDate, saved.Count, errors.Count);

    return Results.Ok(new { saved, errors, date = playDate });
});

app.MapGet("/api/scores", (string? date, ScoreRepository repo) =>
{
    var queryDate = date ?? DateTime.Now.ToString("yyyy-MM-dd");
    return Results.Ok(repo.GetScores(queryDate));
});

app.MapGet("/api/leaderboard", (string? week_offset, ScoreRepository repo) =>
{
    var offset = int.TryParse(week_offset, out var wo) ? wo : 0;
    return Results.Ok(repo.GetLeaderboard(offset));
});

app.MapGet("/api/history", (string? username, ScoreRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(username))
        return Results.Ok(Array.Empty<object>());

    log.LogDebug("History request for {Username}", username);
    return Results.Ok(repo.GetHistory(username.ToLower()));
});

app.MapPost("/api/delete", (DeleteRequest data, ScoreRepository repo) =>
{
    var username = data.Username.Trim().ToLower();
    var game = data.Game;
    var date = data.Date;

    repo.DeleteScore(username, game, date);
    return Results.Ok(new { ok = true });
});

app.Run("http://0.0.0.0:5000");
