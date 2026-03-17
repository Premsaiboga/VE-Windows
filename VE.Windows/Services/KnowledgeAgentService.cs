using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Services;

/// <summary>
/// Knowledge agent service — matches macOS KnowledgeAgentService.
/// Handles knowledge base files (list, upload URL/PDF, delete) and instructions.
/// </summary>
public sealed class KnowledgeAgentService
{
    public static KnowledgeAgentService Instance { get; } = new();
    private KnowledgeAgentService() { }

    private string? GetBaseUrl()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(workspaceId)) return null;
        return $"{baseUrl}/{workspaceId}";
    }

    // --- Knowledge Base Files ---

    public async Task<List<KnowledgeBaseFile>> ListKnowledgeBaseFiles(int page = 1, int limit = 20)
    {
        try
        {
            var url = GetBaseUrl();
            if (url == null) return new();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{url}/knowledge-bases?page={page}&limit={limit}");
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"] as JArray;
            if (data == null) return new();

            var result = new List<KnowledgeBaseFile>();
            foreach (var item in data)
            {
                result.Add(new KnowledgeBaseFile
                {
                    Id = item["_id"]?.ToString() ?? "",
                    OriginalFileName = item["originalFileName"]?.ToString() ?? "Untitled",
                    SourceType = item["sourceType"]?.ToString() ?? item["type"]?.ToString() ?? "",
                    Url = item["url"]?.ToString(),
                    Status = item["status"]?.ToString() ?? "",
                    CreatedAt = item["createdAt"]?.Value<long>() ?? 0
                });
            }

            FileLogger.Instance.Info("KnowledgeAgent", $"Listed {result.Count} KB files (page {page})");
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"ListKnowledgeBaseFiles failed: {ex.Message}");
            return new();
        }
    }

    // --- Upload URL to Knowledge Base ---

    public async Task<bool> UploadUrl(string urlToUpload)
    {
        try
        {
            var baseUrlStr = GetBaseUrl();
            if (baseUrlStr == null) return false;

            var response = await NetworkService.Instance.PostRawAsync(
                $"{baseUrlStr}/knowledge-bases",
                new { type = "url", url = urlToUpload, agent = "knowledgeAgent" });
            FileLogger.Instance.Info("KnowledgeAgent", $"Uploaded URL: {urlToUpload}");
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"UploadUrl failed: {ex.Message}");
            return false;
        }
    }

    // --- Upload File (2-step: get signed URL, then PUT to S3) ---

    public async Task<bool> UploadFile(string filePath)
    {
        try
        {
            var baseUrlStr = GetBaseUrl();
            if (baseUrlStr == null) return false;

            var fileName = System.IO.Path.GetFileName(filePath);
            var batchId = Guid.NewGuid().ToString();
            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);

            // Step 1: Get signed URL
            var response = await NetworkService.Instance.PostRawAsync(
                $"{baseUrlStr}/knowledge-bases/upload-file",
                new { uploadBatchId = batchId, sessionId, originalFileName = fileName, agent = "knowledgeAgent" });
            if (response == null) return false;

            var json = JObject.Parse(response);
            var signedUrl = json["signedUrl"]?.ToString();
            if (string.IsNullOrEmpty(signedUrl)) return false;

            // Step 2: Upload to S3
            var ext = System.IO.Path.GetExtension(filePath).ToLower();
            var mimeType = ext switch
            {
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".json" => "application/json",
                ".csv" => "text/csv",
                _ => "application/octet-stream"
            };

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            using var httpClient = new HttpClient();
            using var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            var s3Response = await httpClient.PutAsync(signedUrl, content);

            FileLogger.Instance.Info("KnowledgeAgent", $"Uploaded file {fileName}: {s3Response.StatusCode}");
            return s3Response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"UploadFile failed: {ex.Message}");
            return false;
        }
    }

    // --- Delete Knowledge Base File ---

    public async Task<bool> DeleteFile(string fileId)
    {
        try
        {
            var baseUrlStr = GetBaseUrl();
            if (baseUrlStr == null) return false;

            return await NetworkService.Instance.DeleteAsync($"{baseUrlStr}/knowledge-bases/{fileId}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"DeleteFile failed: {ex.Message}");
            return false;
        }
    }

    // --- Instructions (matches macOS) ---

    public async Task<List<AIInstruction>> GetInstructions()
    {
        try
        {
            var baseUrlStr = GetBaseUrl();
            if (baseUrlStr == null) return new();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrlStr}/ai-tenant-user-configurations/ai-setup?type=instructions");
            if (response == null) return new();

            var json = JObject.Parse(response);
            var instructions = json["data"]?["instructions"] as JArray ?? json["instructions"] as JArray;
            if (instructions == null) return new();

            var result = new List<AIInstruction>();
            foreach (var item in instructions)
            {
                result.Add(new AIInstruction
                {
                    Id = item["_id"]?.ToString() ?? "",
                    Content = item["content"]?.ToString() ?? "",
                    Platforms = item["platforms"]?.ToString() ?? "",
                    CreatedAt = item["createdAt"]?.Value<long>() ?? 0
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"GetInstructions failed: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> SaveInstruction(string content, string platforms, string? instructionId = null)
    {
        try
        {
            var baseUrlStr = GetBaseUrl();
            if (baseUrlStr == null) return false;

            var body = new Dictionary<string, object>
            {
                ["type"] = "instructions",
                ["data"] = content,
                ["platforms"] = platforms
            };
            if (instructionId != null) body["id"] = instructionId;

            var response = await NetworkService.Instance.PutRawAsync(
                $"{baseUrlStr}/ai-tenant-user-configurations/ai-setup", body);
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"SaveInstruction failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteInstruction(string instructionId)
    {
        try
        {
            var baseUrlStr = GetBaseUrl();
            if (baseUrlStr == null) return false;

            return await NetworkService.Instance.DeleteAsync(
                $"{baseUrlStr}/ai-tenant-user-configurations/ai-setup/instructions/{instructionId}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("KnowledgeAgent", $"DeleteInstruction failed: {ex.Message}");
            return false;
        }
    }
}
