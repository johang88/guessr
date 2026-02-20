namespace Guessr.Models;

public class Score
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Game { get; set; } = "";
    public string? GameNumber { get; set; }
    public double ScoreValue { get; set; }
    public string? RawText { get; set; }
    public string PlayDate { get; set; } = "";
    public string? CreatedAt { get; set; }
}
