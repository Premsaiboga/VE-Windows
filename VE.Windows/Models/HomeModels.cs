namespace VE.Windows.Models;

public class HomeData
{
    public UsageStats? Usage { get; set; }
    public List<AIQuestion> SuggestedQuestions { get; set; } = new();
    public List<RecentActivity> RecentActivities { get; set; } = new();
}

public class UsageStats
{
    public int PredictionsToday { get; set; }
    public int DictationsToday { get; set; }
    public int ChatsToday { get; set; }
    public int MeetingsToday { get; set; }
    public int TotalPredictions { get; set; }
    public int TotalDictations { get; set; }
}

public class AIQuestion
{
    public string Id { get; set; } = "";
    public string Question { get; set; } = "";
    public string Category { get; set; } = "";
}

public class RecentActivity
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class UpcomingMeetingItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Platform { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Organizer { get; set; }
    public List<string> Attendees { get; set; } = new();
}

public class UpcomingMeetingsResponse
{
    public List<UpcomingMeetingItem> Meetings { get; set; } = new();
    public int TotalCount { get; set; }
}

public class MeetingResult
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public MeetingSummary? Summary { get; set; }
    public List<TranscriptionEntry> Transcription { get; set; } = new();
}

public class MeetingSummary
{
    public string Overview { get; set; } = "";
    public List<string> KeyPoints { get; set; } = new();
    public List<string> ActionItems { get; set; } = new();
    public List<string> Decisions { get; set; } = new();
}

public class TranscriptionEntry
{
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
    public TimeSpan Timestamp { get; set; }
    public double Confidence { get; set; }
}
