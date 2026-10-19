namespace PkgDiff.Application.Interfaces;

public interface IOllamaService
{
    Task<string> GenerateTextAsync(
        string prompt,
        string systemMessage = "",
        string model = "phi3",
        OllamaGenerationOptions? options = null);
}
