using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class VoiceService
{
    public static VoiceService Instance { get; } = new();
    private VoiceService() { }

    public async Task<List<VoiceLog>?> GetVoiceLogs(int limit = 20, int page = 1)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("voice_intelligence_ws_api");
        if (baseUrl == null) return null;
        var httpUrl = baseUrl.Replace("wss://", "https://");
        return await NetworkService.Instance.GetAsync<List<VoiceLog>>(
            $"{httpUrl}/logs?limit={limit}&page={page}");
    }

    public async Task<List<PredictionLog>?> GetPredictionLogs(int limit = 20, int page = 1)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("unified_audio_ws_api");
        if (baseUrl == null) return null;
        var httpUrl = baseUrl.Replace("wss://", "https://");
        return await NetworkService.Instance.GetAsync<List<PredictionLog>>(
            $"{httpUrl}/logs?limit={limit}&page={page}");
    }
}
