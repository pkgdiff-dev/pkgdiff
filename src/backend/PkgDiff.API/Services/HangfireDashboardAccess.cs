using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace PkgDiff.API.Services;

public sealed class HangfireDashboardAccess
{
    public const string CookieName = "pkgdiff.hangfire";
    public const string TicketPurpose = "PkgDiff.Hangfire.Ticket.v1";
    public const string CookiePurpose = "PkgDiff.Hangfire.Cookie.v1";
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(30);

    private const char NonceSeparator = '|';

    // Outlives the ticket itself, so a replay inside the ticket lifetime always finds the marker.
    private static readonly DistributedCacheEntryOptions UsedTicketOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
    };

    private readonly ITimeLimitedDataProtector _ticketProtector;
    private readonly ITimeLimitedDataProtector _cookieProtector;
    private readonly IDistributedCache _cache;

    public HangfireDashboardAccess(IDataProtectionProvider provider, IDistributedCache cache)
    {
        _ticketProtector = provider.CreateProtector(TicketPurpose).ToTimeLimitedDataProtector();
        _cookieProtector = provider.CreateProtector(CookiePurpose).ToTimeLimitedDataProtector();
        _cache = cache;
    }

    public string CreateTicket(string userId) =>
        CreateTicket(userId, DateTimeOffset.UtcNow.Add(TicketLifetime));

    public string CreateTicket(string userId, DateTimeOffset expiresAt)
    {
        // The nonce keys the single-use marker independently of the ticket's encoding
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return _ticketProtector.Protect(nonce + NonceSeparator + userId, expiresAt);
    }

    public async Task<string?> RedeemTicketAsync(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return null;

        string payload;
        try
        {
            payload = _ticketProtector.Unprotect(ticket);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var separator = payload.IndexOf(NonceSeparator);
        if (separator <= 0 || separator == payload.Length - 1)
            return null;

        var nonce = payload[..separator];
        var userId = payload[(separator + 1)..];

        // No atomic check-and-set: a same-instant race is bounded by the 60 s ticket lifetime
        var markerKey = "hangfire-ticket-used:" + nonce;
        if (await _cache.GetAsync(markerKey) is not null)
            return null;

        await _cache.SetAsync(markerKey, [1], UsedTicketOptions);
        return userId;
    }

    public string CreateCookieValue(string userId) =>
        _cookieProtector.Protect(userId, DateTimeOffset.UtcNow.Add(CookieLifetime));

    public string? ReadCookieValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var userId = _cookieProtector.Unprotect(value);
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task<bool> IsAuthorizedAsync(HttpContext context, IAdminRoleChecker checker)
    {
        // JWT role claims cannot be revoked, so the bearer path also gets the database Admin check
        if (context.User.IsInRole("Admin"))
        {
            var bearerUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            if (bearerUserId is not null && await checker.IsActiveAdminAsync(bearerUserId))
                return true;
        }

        var userId = ReadCookieValue(context.Request.Cookies[CookieName]);
        if (userId is null)
            return false;

        return await checker.IsActiveAdminAsync(userId);
    }
}
