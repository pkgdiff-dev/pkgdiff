using AngleSharp.Dom;

namespace PkgDiff.Application.Interfaces;

public interface IHtmlDocumentLoader
{
    Task<IDocument?> LoadAsync(string? url, CancellationToken cancellationToken = default);
}
