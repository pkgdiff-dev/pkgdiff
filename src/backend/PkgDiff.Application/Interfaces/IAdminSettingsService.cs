using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminSettingsService
{
    Task<List<SystemSettingDto>> GetSettingsAsync();
    Task<SystemSettingDto> UpdateSettingAsync(int id, UpdateSettingRequest request);
}
