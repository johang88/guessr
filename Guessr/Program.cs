using System.Text.Json;
using Guessr.Data;
using Guessr.Models;
using Guessr.Parsers;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configuration from environment (same defaults as the Python app)
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "guessr_scores.db";
var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";

// Dependency injection
builder.Services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(dbPath));
builder.Services.AddScoped<ScoreRepository>();

// Use snake_case JSON to match the Python API surface exactly
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

// OpenTelemetry — traces and logs exported via OTLP.
// OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_SERVICE_NAME, and OTEL_EXPORTER_OTLP_PROTOCOL
// are read automatically from environment variables by the SDK.
// WithLogging shares the same resource (service name, attributes) as WithTracing.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("guessr")
        .AddEnvironmentVariableDetector())
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithLogging(l => l
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(l =>
{
    l.IncludeScopes = true;
    l.IncludeFormattedMessage = true;
});

var app = builder.Build();
var log = app.Logger;

log.LogInformation("Starting Guessr {Version}, db={DbPath}", appVersion, dbPath);

// Initialise the database schema on startup
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ScoreRepository>().InitializeDb();
}

// ── Routes ───────────────────────────────────────────────────────────────────

// GET / — serve the SPA
app.MapGet("/", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "wwwroot/", "index.html");
    return Results.File(path, "text/html");
});

// GET /health
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
        return Results.Json(new { status = "error", db = ex.Message }, statusCode: 500);
    }
});

// GET /api/version
app.MapGet("/api/version", () => Results.Ok(new { version = appVersion }));

// POST /api/parse
app.MapPost("/api/parse", async (HttpRequest req, ScoreRepository repo) =>
{
    ParseRequest? data;
    try
    {
        data = await req.ReadFromJsonAsync<ParseRequest>();
    }
    catch
    {
        log.LogWarning("Received invalid JSON on /api/parse");
        return Results.BadRequest(new { error = "Invalid JSON" });
    }

    var username = data?.Username?.Trim() ?? "";
    var text = data?.Text ?? "";

    if (string.IsNullOrEmpty(username))
        return Results.BadRequest(new { error = "Username required" });

    if (string.IsNullOrEmpty(text))
        return Results.BadRequest(new { error = "No text provided" });

    var playDate = (data?.Date is { } d && DateTime.TryParse(d, out _) ? d : null)
        ?? GameParsers.ParseDateFromText(text)
        ?? DateTime.Now.ToString("yyyy-MM-dd");

    if (string.Compare(playDate, DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.Ordinal) > 0)
    {
        log.LogWarning("Rejected future-date submission from {Username} for {Date}", username, playDate);
        return Results.BadRequest(new { error = $"Cannot submit scores for a future date ({playDate})" });
    }

    log.LogInformation("Parsing submission from {Username} for {Date}", username, playDate);

    var parsed = GameParsers.ParseAll(text);

    if (parsed.Count == 0)
    {
        log.LogWarning("No games parsed from {Username} submission for {Date}", username, playDate);
        return Results.BadRequest(new { error = "Could not parse any game scores from the text" });
    }

    var (saved, errors) = repo.SaveParsedScores(username.ToLower(), text, playDate, parsed);

    log.LogInformation(
        "Submission from {Username} for {Date}: {Saved} saved, {Errors} duplicate(s)",
        username, playDate, saved.Count, errors.Count);

    return Results.Ok(new { saved, errors, date = playDate });
});

// GET /api/scores?date=YYYY-MM-DD
app.MapGet("/api/scores", (string? date, ScoreRepository repo) =>
{
    var queryDate = date ?? DateTime.Now.ToString("yyyy-MM-dd");
    return Results.Ok(repo.GetScores(queryDate));
});

// GET /api/leaderboard?week_offset=0
app.MapGet("/api/leaderboard", (string? week_offset, ScoreRepository repo) =>
{
    var offset = int.TryParse(week_offset, out var wo) ? wo : 0;
    return Results.Ok(repo.GetLeaderboard(offset));
});

// GET /api/history?username=alice
app.MapGet("/api/history", (string? username, ScoreRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(username))
        return Results.Ok(Array.Empty<object>());

    log.LogDebug("History request for {Username}", username);
    return Results.Ok(repo.GetHistory(username.ToLower()));
});

// POST /api/delete
app.MapPost("/api/delete", async (HttpRequest req, ScoreRepository repo) =>
{
    DeleteRequest? data;
    try
    {
        data = await req.ReadFromJsonAsync<DeleteRequest>();
    }
    catch
    {
        log.LogWarning("Received invalid JSON on /api/delete");
        return Results.BadRequest(new { error = "Missing fields" });
    }

    var username = data?.Username?.Trim()?.ToLower() ?? "";
    var game = data?.Game ?? "";
    var date = data?.Date ?? "";

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(game) || string.IsNullOrEmpty(date))
        return Results.BadRequest(new { error = "Missing fields" });

    repo.DeleteScore(username, game, date);
    return Results.Ok(new { ok = true });
});

app.Run("http://0.0.0.0:5000");
