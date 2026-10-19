using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Content;

public class OllamaServiceRequestTests
{
    private readonly CapturingHandler _handler = new();

    [Fact]
    public async Task Options_are_sent_with_ollama_key_names()
    {
        var options = new OllamaGenerationOptions(
            Temperature: 0.2,
            TopP: 0.9,
            RepeatPenalty: 1.15,
            NumCtx: 8192,
            NumPredict: 900);

        var text = await CreateService().GenerateTextAsync("prompt", "system", "model", options);

        Assert.Equal("ok", text);
        using var body = JsonDocument.Parse(_handler.Body);
        var sent = body.RootElement.GetProperty("options");
        Assert.Equal(0.2, sent.GetProperty("temperature").GetDouble());
        Assert.Equal(0.9, sent.GetProperty("top_p").GetDouble());
        Assert.Equal(1.15, sent.GetProperty("repeat_penalty").GetDouble());
        Assert.Equal(8192, sent.GetProperty("num_ctx").GetInt32());
        Assert.Equal(900, sent.GetProperty("num_predict").GetInt32());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("/api/generate", _handler.Path);
    }

    [Fact]
    public async Task No_options_keeps_the_original_body_shape()
    {
        await CreateService().GenerateTextAsync("prompt", "system", "model");

        using var body = JsonDocument.Parse(_handler.Body);
        var names = body.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["model", "system", "prompt", "stream"], names);
        Assert.False(body.RootElement.TryGetProperty("options", out _));
        Assert.Equal("prompt", body.RootElement.GetProperty("prompt").GetString());
    }

    private OllamaService CreateService()
    {
        var client = new HttpClient(_handler) { BaseAddress = new Uri("http://ollama.test") };
        return new OllamaService(client, NullLogger<OllamaService>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        public string Path { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":\"ok\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}
