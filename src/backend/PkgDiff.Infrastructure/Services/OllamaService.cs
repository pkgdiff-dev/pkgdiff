using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;

    // Base address and timeout come from the client registration; nothing is changed on the client here
    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> GenerateTextAsync(
        string prompt,
        string systemMessage = "",
        string model = "phi3",
        OllamaGenerationOptions? options = null)
    {
        try
        {
            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["system"] = systemMessage,
                ["prompt"] = prompt,
                ["stream"] = false
            };

            if (options != null)
            {
                requestBody["options"] = BuildOptions(options);
            }

            using var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync("/api/generate", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Ollama API Error: {StatusCode} - {Error}", response.StatusCode, error);
                throw new Exception($"Ollama API returned {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);
            
            return responseData.GetProperty("response").GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Local Ollama at {BaseUrl}", _httpClient.BaseAddress);
            throw;
        }
    }

    private static Dictionary<string, object> BuildOptions(OllamaGenerationOptions options)
    {
        var result = new Dictionary<string, object>();
        if (options.Temperature.HasValue) result["temperature"] = options.Temperature.Value;
        if (options.TopP.HasValue) result["top_p"] = options.TopP.Value;
        if (options.RepeatPenalty.HasValue) result["repeat_penalty"] = options.RepeatPenalty.Value;
        if (options.NumCtx.HasValue) result["num_ctx"] = options.NumCtx.Value;
        if (options.NumPredict.HasValue) result["num_predict"] = options.NumPredict.Value;
        return result;
    }
}
