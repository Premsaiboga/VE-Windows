using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class TeamMembersService
{
    public static TeamMembersService Instance { get; } = new();
    private TeamMembersService() { }

    public async Task<List<TeamMember>?> GetTeamMembers()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant-users");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<TeamMember>>($"{baseUrl}/members");
    }

    public async Task<bool> InviteMember(string email, string role = "member")
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant-users");
        if (baseUrl == null) return false;
        var result = await NetworkService.Instance.PostAsync<object>($"{baseUrl}/invite", new { email, role });
        return result != null;
    }

    public async Task<bool> RemoveMember(string memberId)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant-users");
        if (baseUrl == null) return false;
        return await NetworkService.Instance.DeleteAsync($"{baseUrl}/members/{memberId}");
    }
}
