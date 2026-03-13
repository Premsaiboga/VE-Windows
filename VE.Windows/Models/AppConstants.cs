using System.Diagnostics;

namespace VE.Windows.Models;

public enum AppURLType
{
    TermsAndConditions,
    PrivacyPolicy,
    Website,
    HelpCenter,
    Support,
    ViewPlan,
    RenewPlan,
    ContactSupport
}

public static class AppURLs
{
    public static string GetUrl(AppURLType type) => type switch
    {
        AppURLType.TermsAndConditions => "https://www.ve.ai/terms-of-service",
        AppURLType.PrivacyPolicy => "https://www.ve.ai/privacy-policy",
        AppURLType.Website => "https://www.ve.ai",
        AppURLType.HelpCenter => "https://help.ve.ai/en/",
        AppURLType.Support => "https://www.ve.ai/contact-us",
        AppURLType.ViewPlan => "https://www.ve.ai/settings/pricing",
        AppURLType.RenewPlan => "https://www.ve.ai/settings/pricing/",
        AppURLType.ContactSupport => "mailto:support@ve.ai",
        _ => ""
    };

    public static void Open(AppURLType type)
    {
        var url = GetUrl(type);
        if (string.IsNullOrEmpty(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static string SanitizeForUrl(string text)
    {
        return text
            .Replace("%", " percent")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");
    }
}

public static class OnboardingVideos
{
    public const string AskVe = "https://us.images.ve.ai/public/video/Askve.webm";
    public const string Notes = "https://us.images.ve.ai/public/video/notes.webm";
}

public static class AppConstantsData
{
    public const string S3BaseUrl = "https://us.images.ve.ai/public/desktop-app";
    public const string BundleIdentifier = "com.veai.windows";
}
