using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.Infrastructure.Services;

public class SystemSettingsReader : ISystemSettingsReader
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISecretProtector _protector;
    private readonly ILogger<SystemSettingsReader> _logger;

    public SystemSettingsReader(IUnitOfWork unitOfWork, ISecretProtector protector, ILogger<SystemSettingsReader> logger)
    {
        _unitOfWork = unitOfWork;
        _protector = protector;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key)
    {
        var setting = (await _unitOfWork.SystemSettings.FindAsync(s => s.Key == key)).FirstOrDefault();
        if (setting == null)
        {
            return null;
        }

        if (!_protector.IsProtected(setting.Value))
        {
            return setting.Value;
        }

        try
        {
            return _protector.Unprotect(setting.Value);
        }
        catch (CryptographicException)
        {
            // The row is left as stored so a rotated or restored key can still read it later
            _logger.LogWarning("Setting {Key} could not be decrypted with the configured SETTINGS_ENCRYPTION_KEY; treating it as not configured", key);
            return null;
        }
    }
}
