using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class KnowledgeAgentService
{
    public static KnowledgeAgentService Instance { get; } = new();
    private KnowledgeAgentService() { }

    // Instructions
    public async Task<List<Instruction>?> GetInstructions()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<Instruction>>($"{baseUrl}/instructions");
    }

    public async Task<Instruction?> CreateInstruction(string title, string content)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.PostAsync<Instruction>(
            $"{baseUrl}/instructions", new { title, content });
    }

    public async Task<Instruction?> UpdateInstruction(string id, string title, string content, bool isActive)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.PutAsync<Instruction>(
            $"{baseUrl}/instructions/{id}", new { title, content, isActive });
    }

    public async Task<bool> DeleteInstruction(string id)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return false;
        return await NetworkService.Instance.DeleteAsync($"{baseUrl}/instructions/{id}");
    }

    // Knowledge Bases
    public async Task<List<KnowledgeBase>?> GetKnowledgeBases()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<KnowledgeBase>>($"{baseUrl}/knowledge-bases");
    }
}
