using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface ISocialMediaService
{
    Task<SocialPostResult> PostToTwitterAsync(string content);
    Task<SocialPostResult> PostToLinkedInAsync(string content);
    Task<bool> IsConfiguredAsync(PkgDiff.Domain.Entities.SocialMediaPlatform platform);
    Task<(string AuthUrl, string State)?> GetLinkedInAuthUrlAsync(string redirectUri, string userId);
    Task<(bool Success, string? ErrorMessage)> HandleLinkedInCallbackAsync(string code, string state, string userId);
}
