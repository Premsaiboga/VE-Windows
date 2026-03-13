using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class WorkspaceService
{
    public static WorkspaceService Instance { get; } = new();
    private WorkspaceService() { }

    public async Task<List<WorkspaceInfo>?> GetWorkspaces()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<WorkspaceInfo>>($"{baseUrl}/workspaces");
    }

    public async Task<bool> SwitchWorkspace(string workspaceId)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant");
        if (baseUrl == null) return false;
        var result = await NetworkService.Instance.PostAsync<object>(
            $"{baseUrl}/workspaces/switch", new { workspaceId });
        return result != null;
    }
}
