using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// Voice profile enrollment service.
/// User records voice samples which are sent to the backend for voice profile creation.
/// Currently disabled (matches macOS VoiceEnrollmentService which is also disabled/placeholder).
/// </summary>
public sealed class VoiceEnrollmentService
{
    public static VoiceEnrollmentService Instance { get; } = new();

    public bool IsEnrolled { get; private set; }
    public bool IsEnrolling { get; private set; }

    private VoiceEnrollmentService() { }

    /// <summary>
    /// Start voice enrollment — record voice sample and send to backend.
    /// </summary>
    public async Task<bool> EnrollVoice(byte[] audioData)
    {
        if (IsEnrolling) return false;
        IsEnrolling = true;

        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("unified_audio_ws_api");
            if (baseUrl == null) return false;

            var tenantId = AuthManager.Instance.Storage.TenantId;
            if (string.IsNullOrEmpty(tenantId)) return false;

            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
            var httpUrl = baseUrl.Replace("wss://", "https://");
            var url = $"{httpUrl}/{tenantId}/{sessionId}/voice-enrollment";

            var base64Audio = Convert.ToBase64String(audioData);
            var response = await NetworkService.Instance.PostAsync<VoiceEnrollmentResponse>(
                url,
                new { audio_data = base64Audio });

            if (response?.Success == true)
            {
                IsEnrolled = true;
                FileLogger.Instance.Info("VoiceEnrollment", "Voice enrollment successful");
                return true;
            }

            FileLogger.Instance.Warning("VoiceEnrollment", "Voice enrollment failed");
            return false;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceEnrollment", $"Enrollment error: {ex.Message}");
            return false;
        }
        finally
        {
            IsEnrolling = false;
        }
    }

    /// <summary>
    /// Check if voice is already enrolled.
    /// </summary>
    public async Task CheckEnrollmentStatus()
    {
        // Placeholder — will be implemented when backend endpoint is available
        FileLogger.Instance.Debug("VoiceEnrollment", "Enrollment check — feature not yet active");
    }

    private class VoiceEnrollmentResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}
