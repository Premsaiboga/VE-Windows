using System.ComponentModel;
using VE.Windows.Helpers;

namespace VE.Windows.Services;

/// <summary>
/// Prediction feedback loop - 15-second window after prediction success.
/// Monitors Enter key presses and sends { id, used_text } feedback.
/// Equivalent to macOS PredictionFeedbackService.
/// </summary>
public sealed class PredictionFeedbackService : IDisposable
{
    public static PredictionFeedbackService Instance { get; } = new();

    private string? _activePredictionId;
    private Timer? _feedbackTimer;
    private const int FeedbackWindowSeconds = 15;

    private PredictionFeedbackService()
    {
        // Listen for Enter key presses via keyboard hook
        Managers.KeyboardHookManager.Instance.OnEnterPressed += OnEnterPressed;
    }

    public void OnPredictionSuccess(string predictionId)
    {
        _activePredictionId = predictionId;

        // Start 15-second feedback window
        _feedbackTimer?.Dispose();
        _feedbackTimer = new Timer(OnFeedbackWindowExpired, null,
            TimeSpan.FromSeconds(FeedbackWindowSeconds), Timeout.InfiniteTimeSpan);

        FileLogger.Instance.Debug("PredictionFeedback", $"Feedback window opened for prediction: {predictionId}");
    }

    private void OnEnterPressed(object? sender, EventArgs e)
    {
        if (_activePredictionId == null) return;

        var predictionId = _activePredictionId;
        _activePredictionId = null;
        _feedbackTimer?.Dispose();
        _feedbackTimer = null;

        // Capture clipboard content as the text user actually sent
        Task.Run(async () =>
        {
            try
            {
                // Small delay to let clipboard update
                await Task.Delay(200);

                string? usedText = null;
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    usedText = System.Windows.Clipboard.GetText();
                });

                if (!string.IsNullOrEmpty(usedText))
                {
                    var baseUrl = BaseURLService.Instance.GetBaseUrl("unified_audio_ws_api");
                    if (baseUrl != null)
                    {
                        var httpUrl = baseUrl.Replace("wss://", "https://");
                        await NetworkService.Instance.PostAsync<object>(
                            $"{httpUrl}/feedback",
                            new { id = predictionId, used_text = usedText });

                        FileLogger.Instance.Info("PredictionFeedback",
                            $"Feedback sent for {predictionId}: {usedText.Length} chars");
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("PredictionFeedback", $"Failed to send feedback: {ex.Message}");
            }
        });
    }

    private void OnFeedbackWindowExpired(object? state)
    {
        _activePredictionId = null;
        _feedbackTimer?.Dispose();
        _feedbackTimer = null;
    }

    public void Dispose()
    {
        _feedbackTimer?.Dispose();
    }
}
