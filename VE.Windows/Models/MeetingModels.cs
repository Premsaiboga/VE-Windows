using Newtonsoft.Json.Linq;

namespace VE.Windows.Models;

// --- Meeting List ---

public class MeetingListItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public double CreatedAt { get; set; }
    public string Status { get; set; } = "";
    public bool IsTranscription { get; set; }

    public DateTime CreatedDate => DateTimeOffset.FromUnixTimeSeconds((long)CreatedAt).LocalDateTime;

    public string FormattedDate
    {
        get
        {
            var date = CreatedDate;
            if (date.Date == DateTime.Today) return "Today";
            if (date.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
            if (date.Date == DateTime.Today.AddDays(1)) return "Tomorrow";
            return date.ToString("MMMM d");
        }
    }

    public string FormattedTime => CreatedDate.ToString("h:mm tt");
}

public class MeetingListResponse
{
    public int CurrentPage { get; set; }
    public List<MeetingListItem> Data { get; set; } = new();
}

// --- Meeting Summary ---

public class MeetingSummaryData
{
    public MeetingData? MeetingData { get; set; }
    public string? TranscriptionSummary { get; set; }
}

public class MeetingData
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public double? CreatedAt { get; set; }
    public string? Status { get; set; }
    public string? TranscriptionSource { get; set; }

    public string FormattedDate
    {
        get
        {
            if (CreatedAt == null) return "";
            var date = DateTimeOffset.FromUnixTimeSeconds((long)CreatedAt.Value).LocalDateTime;
            return date.ToString("ddd, MMM d, yyyy");
        }
    }

    public string FormattedTime
    {
        get
        {
            if (CreatedAt == null) return "";
            var date = DateTimeOffset.FromUnixTimeSeconds((long)CreatedAt.Value).LocalDateTime;
            return date.ToString("HH:mm");
        }
    }
}

// --- Transcriptions ---

public class TranscriptionItem
{
    public string Id { get; set; } = "";
    public string MeetingId { get; set; } = "";
    public string SpeakerName { get; set; } = "";
    public string Transcript { get; set; } = "";
    public double CreatedAt { get; set; }

    public string FormattedTime
    {
        get
        {
            var date = DateTimeOffset.FromUnixTimeSeconds((long)CreatedAt).LocalDateTime;
            return date.ToString("HH:mm:ss");
        }
    }
}

public class TranscriptionListResponse
{
    public List<TranscriptionItem> Data { get; set; } = new();
}

// --- Analytics ---

public class MeetingAnalyticsData
{
    public MeetingMetadata? Metadata { get; set; }
    public string? Summary { get; set; }
    public List<MeetingChapter> Chapters { get; set; } = new();
    public List<MeetingHighlight> Highlights { get; set; } = new();
    public List<MeetingActionItem> ActionItems { get; set; } = new();
    public List<MeetingDecisionItem> Decisions { get; set; } = new();
    public List<MeetingOpenQuestion> OpenQuestions { get; set; } = new();
    public List<MeetingParticipantItem> Participants { get; set; } = new();
    public bool AnalyticsGenerated { get; set; }

    public static MeetingAnalyticsData ParseFromJson(JObject json)
    {
        var result = new MeetingAnalyticsData();
        result.AnalyticsGenerated = json["analytics_generated"]?.Value<bool>() ?? false;
        result.Summary = json["summary"]?.Value<string>();

        // Metadata
        var meta = json["meeting_metadata"] ?? json["meetingMetadata"];
        if (meta != null)
        {
            result.Metadata = new MeetingMetadata
            {
                Title = meta["title"]?.Value<string>(),
                OverallReadScore = meta["overall_read_score"]?.Value<int>(),
                OverallEngagementScore = meta["overall_engagement_score"]?.Value<int>(),
                OverallSentimentScore = meta["overall_sentiment_score"]?.Value<int>()
            };
        }

        // Chapters
        var chapters = json["chapters"] as JArray;
        if (chapters != null)
        {
            foreach (var ch in chapters)
            {
                result.Chapters.Add(new MeetingChapter
                {
                    Title = ch["title"]?.Value<string>(),
                    Summary = ch["summary"]?.Value<string>(),
                    SubTopics = ch["sub_topics"]?.ToObject<List<string>>() ?? new()
                });
            }
        }

        // Highlights
        var highlights = json["highlights"] as JArray;
        if (highlights != null)
        {
            foreach (var h in highlights)
            {
                result.Highlights.Add(new MeetingHighlight
                {
                    Type = h["type"]?.Value<string>(),
                    Text = h["text"]?.Value<string>(),
                    Engagement = h["engagement"]?.Value<string>()
                });
            }
        }

        // Action items
        var actions = (json["action_items"] ?? json["actionItems"]) as JArray;
        if (actions != null)
        {
            foreach (var a in actions)
            {
                var text = a["text"]?.Value<string>() ?? a["item"]?.Value<string>();
                if (!string.IsNullOrEmpty(text))
                    result.ActionItems.Add(new MeetingActionItem { Text = text });
            }
        }

        // Decisions
        var decisions = json["decisions"] as JArray;
        if (decisions != null)
        {
            foreach (var d in decisions)
            {
                var text = d["text"]?.Value<string>() ?? d["decision"]?.Value<string>();
                if (!string.IsNullOrEmpty(text))
                    result.Decisions.Add(new MeetingDecisionItem { Text = text });
            }
        }

        // Open questions
        var questions = (json["open_questions"] ?? json["openQuestions"]) as JArray;
        if (questions != null)
        {
            foreach (var q in questions)
            {
                result.OpenQuestions.Add(new MeetingOpenQuestion
                {
                    Text = q["text"]?.Value<string>() ?? q["question"]?.Value<string>(),
                    Asker = q["asker"]?.Value<string>() ?? q["asked_by"]?.Value<string>()
                });
            }
        }

        // Participants
        var participants = json["participants"] as JArray;
        if (participants != null)
        {
            foreach (var p in participants)
            {
                var scores = p["scores"] as JObject;
                result.Participants.Add(new MeetingParticipantItem
                {
                    Name = p["name"]?.Value<string>() ?? p["speaker"]?.Value<string>(),
                    TalkTimePercentage = p["talk_time_percentage"]?.Value<int>()
                        ?? p["talkTimePercentage"]?.Value<int>(),
                    ParticipantScore = scores?["participant_score"]?.Value<int>()
                        ?? scores?["read_score"]?.Value<int>(),
                    Engagement = scores?["engagement"]?.Value<int>()
                        ?? scores?["engagement_score"]?.Value<int>(),
                    Sentiment = scores?["sentiment"]?.Value<int>()
                        ?? scores?["sentiment_score"]?.Value<int>()
                });
            }
        }

        return result;
    }
}

public class MeetingMetadata
{
    public string? Title { get; set; }
    public int? OverallReadScore { get; set; }
    public int? OverallEngagementScore { get; set; }
    public int? OverallSentimentScore { get; set; }
}

public class MeetingChapter
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public List<string> SubTopics { get; set; } = new();
}

public class MeetingHighlight
{
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? Engagement { get; set; }
}

public class MeetingActionItem
{
    public string Text { get; set; } = "";
}

public class MeetingDecisionItem
{
    public string Text { get; set; } = "";
}

public class MeetingOpenQuestion
{
    public string? Text { get; set; }
    public string? Asker { get; set; }
}

public class MeetingParticipantItem
{
    public string? Name { get; set; }
    public int? TalkTimePercentage { get; set; }
    public int? ParticipantScore { get; set; }
    public int? Engagement { get; set; }
    public int? Sentiment { get; set; }
}