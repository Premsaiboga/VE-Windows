namespace VE.Windows.Models;

public class VoiceLog
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public double Duration { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = "";
}

public class PredictionLog
{
    public string Id { get; set; } = "";
    public string PredictedText { get; set; } = "";
    public string? ActualText { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool WasUsed { get; set; }
    public string? ApplicationContext { get; set; }
}
