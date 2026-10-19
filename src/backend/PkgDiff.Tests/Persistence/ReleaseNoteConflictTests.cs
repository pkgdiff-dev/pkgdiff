using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Persistence;

public class ReleaseNoteConflictTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();

    private PackageCacheService CreateService() => new(
        _unitOfWork, null!, new ReleaseNotesRenderer(), null!, new CachedLookups(_unitOfWork, new MemoryCache(new MemoryCacheOptions())), null!, NullLogger<PackageCacheService>.Instance);

    private Task<CachedReleaseNote> StoredByAnotherRequestAsync(string notes) =>
        _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote
        {
            CachedPackageId = 7,
            Version = "1.2.0",
            ReleaseNotes = notes,
            IsEmpty = notes.Length == 0,
            CachedAt = new DateTime(2026, 9, 1)
        });

    [Fact]
    public async Task A_version_another_request_stored_meanwhile_gets_these_notes_instead_of_a_second_row()
    {
        var existing = await StoredByAnotherRequestAsync("");
        _unitOfWork.DuplicateOnSaveChangesCall = 1;
        var scraped = new CachedReleaseNote
        {
            CachedPackageId = 7,
            Version = "1.2.0",
            ReleaseNotes = "- Fixed a crash",
            ParsedNotesJson = "[]",
            ReleaseNotesSource = "GitHub",
            IsEmpty = false,
            CachedAt = new DateTime(2026, 10, 1),
            ExpiresAt = new DateTime(2026, 12, 30)
        };

        await CreateService().SaveReleaseNoteAsync(scraped);

        Assert.Equal("- Fixed a crash", existing.ReleaseNotes);
        Assert.Equal("GitHub", existing.ReleaseNotesSource);
        Assert.False(existing.IsEmpty);
        Assert.Equal(new DateTime(2026, 10, 1), existing.CachedAt);
        Assert.Equal(existing.Id, scraped.Id);
        Assert.Equal(2, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task An_empty_placeholder_does_not_replace_notes_another_request_stored()
    {
        var existing = await StoredByAnotherRequestAsync("- Fixed a crash");
        _unitOfWork.DuplicateOnSaveChangesCall = 1;

        await CreateService().SaveReleaseNoteAsync(new CachedReleaseNote
        {
            CachedPackageId = 7,
            Version = "1.2.0",
            ReleaseNotes = string.Empty,
            IsEmpty = true,
            CachedAt = new DateTime(2026, 10, 1)
        });

        Assert.Equal("- Fixed a crash", existing.ReleaseNotes);
        Assert.False(existing.IsEmpty);
        Assert.Equal(new DateTime(2026, 9, 1), existing.CachedAt);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }
}
