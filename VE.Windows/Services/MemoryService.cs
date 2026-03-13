using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class MemoryService
{
    public static MemoryService Instance { get; } = new();
    private MemoryService() { }

    public async Task<List<MemoryItem>?> GetMemories(int limit = 50, int page = 1)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("galleries_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<MemoryItem>>(
            $"{baseUrl}/memories?limit={limit}&page={page}");
    }

    public async Task<bool> DeleteMemory(string id)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("galleries_api");
        if (baseUrl == null) return false;
        return await NetworkService.Instance.DeleteAsync($"{baseUrl}/memories/{id}");
    }
}
