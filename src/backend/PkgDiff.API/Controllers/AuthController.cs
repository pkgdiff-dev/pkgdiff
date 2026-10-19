using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using PkgDiff.API.Extensions;
using PkgDiff.API.Services;
using PkgDiff.Application.Services;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const int MaxFailuresPerNameAndAddress = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IRateLimitCounterStore _counters;
    private readonly IDistributedCache _cache;

    public AuthController(
        AuthService authService,
        ILogger<AuthController> logger,
        IRateLimitCounterStore counters,
        IDistributedCache cache)
    {
        _authService = authService;
        _logger = logger;
        _counters = counters;
        _cache = cache;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Blocked per name and address: someone failing elsewhere cannot keep the account locked
            var key = $"{request.Username.Trim().ToLowerInvariant()}:{HttpContext.GetClientIpAddress()}";
            if (await _cache.GetAsync("login-block:" + key) != null)
                return StatusCode(429, new { error = "Too many failed sign-ins. Please wait a few minutes." });

            var response = await _authService.LoginAsync(request);

            if (response == null)
            {
                var failures = await _counters.IncrementAsync("login-fail:" + key, FailureWindow);
                if (failures >= MaxFailuresPerNameAndAddress)
                {
                    await _cache.SetAsync("login-block:" + key, new byte[] { 1 },
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = FailureWindow });
                    _logger.LogWarning("Sign-in blocked for 15 minutes after {Failures} failures from one address", failures);
                }
                return Unauthorized(new { error = "Invalid username or password" });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { error = "An error occurred during login" });
        }
    }

    [HttpPost("register")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Users.Create)]
    public async Task<ActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var (statusCode, errors) = await _authService.RegisterAsync(
                request.Username,
                request.Email,
                request.Password,
                request.Role,
                User.IsInRole(RoleAssignmentRules.AdminRole),
                User.FindAll(RolePermissionLookup.PermissionClaimType).Select(c => c.Value).ToList());

            return statusCode switch
            {
                200 => Ok(new { message = "User registered successfully" }),
                403 => StatusCode(403, new { error = errors[0] }),
                _ => BadRequest(new { errors })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new { error = "An error occurred during registration" });
        }
    }
}

public class RegisterRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string Email { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    public string? Role { get; set; }
}
