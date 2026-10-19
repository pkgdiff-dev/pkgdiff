using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Security;

namespace PkgDiff.Infrastructure.Services;

public class HtmlDocumentLoader : IHtmlDocumentLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HtmlDocumentLoader> _logger;
    private readonly IBrowsingContext _browsingContext;

    public HtmlDocumentLoader(IHttpClientFactory httpClientFactory, ILogger<HtmlDocumentLoader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        // No loader configured: AngleSharp only parses the HTML we fetched through the guarded client.
        _browsingContext = BrowsingContext.New(Configuration.Default);
    }

    public async Task<IDocument?> LoadAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientNames.ExternalHtml);
        try
        {
            // Buffered read, so the client's MaxResponseContentBufferSize cap applies.
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (HtmlNestingGuard.IsTooDeep(html))
            {
                _logger.LogWarning("Refused a page nested deeper than {MaxDepth} elements from {Host}", HtmlNestingGuard.MaxDepth, uri.Host);
                return null;
            }

            var finalUrl = (response.RequestMessage?.RequestUri ?? uri).ToString();
            return await _browsingContext.OpenAsync(res => res.Content(html).Address(finalUrl), cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SsrfBlockedException)
        {
            _logger.LogWarning("Blocked outbound fetch to {Host}", uri.Host);
            return null;
        }
    }
}
