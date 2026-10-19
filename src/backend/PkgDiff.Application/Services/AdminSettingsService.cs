using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminSettingsService : IAdminSettingsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISecretProtector _secretProtector;

    public AdminSettingsService(IUnitOfWork unitOfWork, ISecretProtector secretProtector)
    {
        _unitOfWork = unitOfWork;
        _secretProtector = secretProtector;
    }

    public async Task<List<SystemSettingDto>> GetSettingsAsync()
    {
        var settings = await _unitOfWork.SystemSettings.GetAllAsync();
        return settings
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Key)
            .Select(ToSettingDto)
            .ToList();
    }

    public async Task<SystemSettingDto> UpdateSettingAsync(int id, UpdateSettingRequest request)
    {
        var entity = await _unitOfWork.SystemSettings.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Setting with id {id} not found");

        if (entity.IsSecret)
        {
            // Secrets are write-only: an empty value keeps the stored secret, Clear removes it
            if (request.Clear)
            {
                entity.Value = string.Empty;
            }
            else if (!string.IsNullOrEmpty(request.Value))
            {
                entity.Value = _secretProtector.Protect(request.Value);
            }
        }
        else
        {
            entity.Value = request.Value ?? string.Empty;
        }

        await _unitOfWork.SystemSettings.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        if (!entity.IsSecret && entity.Key == "AutoPilot.DailyFrequency" && int.TryParse(request.Value, out int freq))
        {
            var cron = GetCronExpressionForFrequency(freq);
            RecurringJob.AddOrUpdate<IAutoPilotService>(
                "autopilot-blog-generator",
                job => job.RunAutoPilotJobAsync(),
                cron);
        }

        return ToSettingDto(entity);
    }

    private static SystemSettingDto ToSettingDto(SystemSettings s) => new()
    {
        Id = s.Id,
        Key = s.Key,
        Value = s.IsSecret ? string.Empty : s.Value,
        HasValue = !string.IsNullOrEmpty(s.Value),
        Description = s.Description,
        DataType = s.DataType,
        Category = s.Category,
        IsSecret = s.IsSecret
    };

    public static string GetCronExpressionForFrequency(int dailyFrequency)
    {
        return dailyFrequency switch
        {
            1 => "0 12 * * *",
            2 => "0 0,12 * * *",
            3 => "0 8,14,20 * * *",
            4 => "0 0,6,12,18 * * *",
            6 => "0 0,4,8,12,16,20 * * *",
            8 => "0 0,3,6,9,12,15,18,21 * * *",
            12 => "0 0,2,4,6,8,10,12,14,16,18,20,22 * * *",
            _ when dailyFrequency >= 24 => "0 * * * *",
            _ => "0 8,14,20 * * *"
        };
    }
}
