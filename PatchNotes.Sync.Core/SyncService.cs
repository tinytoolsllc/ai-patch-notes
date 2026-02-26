using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchNotes.Data;
using PatchNotes.Sync.Core.GitHub;

namespace PatchNotes.Sync.Core;

/// <summary>
/// Service for syncing package releases from GitHub.
/// Designed to be reusable in both console app and Azure Functions.
/// </summary>
public class SyncService
{
    private readonly PatchNotesDbContext _db;
    private readonly IGitHubClient _github;
    private readonly ILogger<SyncService> _logger;
    private readonly ChangelogResolver? _changelogResolver;

    public SyncService(
        PatchNotesDbContext db,
        IGitHubClient github,
        ILogger<SyncService> logger,
        ChangelogResolver? changelogResolver = null)
    {
        _db = db;
        _github = github;
        _logger = logger;
        _changelogResolver = changelogResolver;
    }

    /// <summary>
    /// Syncs all tracked packages, fetching new releases from GitHub.
    /// </summary>
    /// <param name="includeExistingWithoutSummary">If true, includes existing releases missing summaries in results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success/failure and statistics.</returns>
    public async Task<SyncResult> SyncAllAsync(
        bool includeExistingWithoutSummary = false,
        CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();
        var packages = await _db.Packages.ToListAsync(cancellationToken);

        _logger.LogInformation("Starting sync for {Count} packages", packages.Count);

        foreach (var package in packages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Sync cancelled");
                break;
            }

            try
            {
                var packageResult = await SyncPackageAsync(package, includeExistingWithoutSummary, cancellationToken);
                result.PackagesSynced++;
                result.ReleasesAdded += packageResult.ReleasesAdded;
                result.ReleasesNeedingSummary.AddRange(packageResult.ReleasesNeedingSummary);

                if (packageResult.ReleasesAdded > 0)
                {
                    _logger.LogInformation(
                        "Synced {Package}: {Count} new releases, {NeedSummary} need summaries",
                        package.Name,
                        packageResult.ReleasesAdded,
                        packageResult.ReleasesNeedingSummary.Count);
                }
                else
                {
                    _logger.LogDebug("Synced {Package}: no new releases", package.Name);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new SyncError(package.Name, ex.Message));
                _logger.LogError(ex, "Failed to sync {Package}", package.Name);

                // Discard pending changes from the failed package to prevent
                // them from being saved by a subsequent package's SaveChanges
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                {
                    if (entry.State == EntityState.Added)
                        entry.State = EntityState.Detached;
                    else if (entry.State == EntityState.Modified)
                    {
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                    }
                }
            }
        }

        _logger.LogInformation(
            "Sync complete: {Packages} packages, {Releases} new releases, {NeedSummary} need summaries, {Errors} errors",
            result.PackagesSynced,
            result.ReleasesAdded,
            result.ReleasesNeedingSummary.Count,
            result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Syncs a single package, fetching new releases from GitHub.
    /// </summary>
    /// <param name="package">The package to sync.</param>
    /// <param name="includeExistingWithoutSummary">If true, includes existing releases missing summaries in results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with count of releases added and releases needing summaries.</returns>
    public async Task<PackageSyncResult> SyncPackageAsync(
        Package package,
        bool includeExistingWithoutSummary = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(package.GithubOwner) || string.IsNullOrEmpty(package.GithubRepo))
        {
            _logger.LogWarning(
                "Skipping {Package}: missing GitHub owner/repo",
                package.Name);
            return new PackageSyncResult(0);
        }

        var since = package.LastFetchedAt;
        var fetchedAt = DateTimeOffset.UtcNow;
        var releasesAdded = 0;
        var releasesNeedingSummary = new List<Release>();

        // Get existing release tags to avoid duplicates
        var existingTags = await _db.Releases
            .Where(r => r.PackageId == package.Id)
            .Select(r => r.Tag)
            .ToHashSetAsync(cancellationToken);

        // Batch-collect releases locally before adding to the change tracker.
        // This prevents orphaned entities if processing fails mid-loop.
        var newReleases = new List<Release>();

        await foreach (var ghRelease in _github.GetAllReleasesAsync(
            package.GithubOwner,
            package.GithubRepo,
            cancellationToken))
        {
            // Skip drafts
            if (ghRelease.Draft)
                continue;

            // Skip releases without a published date
            if (!ghRelease.PublishedAt.HasValue)
                continue;

            // Skip releases that don't match the tag prefix filter
            if (!string.IsNullOrEmpty(package.TagPrefix) &&
                !ghRelease.TagName.StartsWith(package.TagPrefix, StringComparison.Ordinal))
                continue;

            // If we have a last fetched date, skip older releases
            // GitHub returns releases newest-first, so once we hit an old one, we can stop
            if (since.HasValue && ghRelease.PublishedAt.Value <= since.Value)
                break;

            // Skip if we already have this release
            if (existingTags.Contains(ghRelease.TagName))
                continue;

            var body = ghRelease.Body;

            // Follow cross-repo release links (e.g. dotnet/runtime → dotnet/core → dotnet/dotnet)
            if (_changelogResolver != null && ChangelogResolver.ExtractGitHubReleaseLink(body) != null)
            {
                try
                {
                    var followed = await _changelogResolver.FollowReleaseLinksAsync(body, cancellationToken: cancellationToken);
                    if (followed != null && followed != body)
                    {
                        _logger.LogInformation(
                            "Followed release links for {Package} {Tag}",
                            package.Name, ghRelease.TagName);
                        body = followed;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to follow release links for {Package} {Tag}, keeping original body",
                        package.Name, ghRelease.TagName);
                }
            }

            // Resolve external changelog references
            if (_changelogResolver != null && ChangelogResolver.IsChangelogReference(body))
            {
                try
                {
                    var resolved = await _changelogResolver.ResolveAsync(
                        package.GithubOwner, package.GithubRepo,
                        ghRelease.TagName, body, cancellationToken);

                    if (resolved != null)
                    {
                        _logger.LogInformation(
                            "Resolved changelog reference for {Package} {Tag}",
                            package.Name, ghRelease.TagName);
                        body = resolved;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to resolve changelog for {Package} {Tag}, keeping original body",
                        package.Name, ghRelease.TagName);
                }
            }

            var parsed = VersionParser.ParseTagValues(ghRelease.TagName);
            var release = new Release
            {
                PackageId = package.Id,
                Tag = ghRelease.TagName,
                Title = ghRelease.Name,
                Body = body,
                PublishedAt = ghRelease.PublishedAt.Value,
                FetchedAt = fetchedAt,
                MajorVersion = parsed.MajorVersion,
                MinorVersion = parsed.MinorVersion,
                PatchVersion = parsed.PatchVersion,
                IsPrerelease = parsed.IsPrerelease
            };

            newReleases.Add(release);
            existingTags.Add(ghRelease.TagName);
            releasesAdded++;

            // New releases always need summaries
            releasesNeedingSummary.Add(release);
        }

        // Add all releases to the change tracker only after the loop
        // completes successfully — no orphans if processing threw mid-loop
        _db.Releases.AddRange(newReleases);
        package.LastFetchedAt = fetchedAt;
        await _db.SaveChangesAsync(cancellationToken);

        // Optionally include existing releases that are missing summaries
        if (includeExistingWithoutSummary)
        {
            var existingWithoutSummary = await _db.Releases
                .Where(r => r.PackageId == package.Id && (r.SummaryStale))
                .Where(r => !releasesNeedingSummary.Select(x => x.Id).Contains(r.Id))
                .ToListAsync(cancellationToken);

            releasesNeedingSummary.AddRange(existingWithoutSummary);
        }

        return new PackageSyncResult(releasesAdded, releasesNeedingSummary);
    }

    /// <summary>
    /// Syncs a single repository by owner/repo, creating the package if it doesn't exist.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="repo">GitHub repository name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with count of releases added and releases needing summaries.</returns>
    public async Task<PackageSyncResult> SyncRepoAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        var package = await _db.Packages
            .FirstOrDefaultAsync(
                p => p.GithubOwner == owner && p.GithubRepo == repo,
                cancellationToken);

        var isNewPackage = package == null;
        if (isNewPackage)
        {
            package = new Package
            {
                Name = repo,
                Url = $"https://github.com/{owner}/{repo}",
                GithubOwner = owner,
                GithubRepo = repo,
            };
            _db.Packages.Add(package);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Created package {Owner}/{Repo}", owner, repo);
        }

        try
        {
            return await SyncPackageAsync(package!, cancellationToken: cancellationToken);
        }
        catch when (isNewPackage)
        {
            _logger.LogWarning("Sync failed for new package {Owner}/{Repo}, removing phantom package", owner, repo);
            _db.Packages.Remove(package!);
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Gets all releases that need summary generation across all packages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of releases needing summaries.</returns>
    public async Task<List<Release>> GetReleasesNeedingSummaryAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Releases
            .Include(r => r.Package)
            .Where(r => r.SummaryStale)
            .OrderByDescending(r => r.PublishedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets releases needing summary generation for a specific package.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of releases needing summaries.</returns>
    public async Task<List<Release>> GetReleasesNeedingSummaryAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Releases
            .Where(r => r.PackageId == packageId && (r.SummaryStale))
            .OrderByDescending(r => r.PublishedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Backfills denormalized version fields for all existing releases.
    /// Parses the Tag and updates MajorVersion, MinorVersion, PatchVersion, and IsPrerelease.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    /// <returns>Number of releases updated.</returns>
    public async Task<int> BackfillVersionFieldsAsync(CancellationToken cancellationToken = default)
    {
        var releases = await _db.Releases
            .Where(r => r.MajorVersion == 0 && r.MinorVersion == 0 && r.PatchVersion == 0)
            .ToListAsync(cancellationToken);
        var updated = 0;

        foreach (var release in releases)
        {
            var parsed = VersionParser.ParseTagValues(release.Tag);
            if (release.MajorVersion != parsed.MajorVersion
                || release.MinorVersion != parsed.MinorVersion
                || release.PatchVersion != parsed.PatchVersion
                || release.IsPrerelease != parsed.IsPrerelease)
            {
                release.MajorVersion = parsed.MajorVersion;
                release.MinorVersion = parsed.MinorVersion;
                release.PatchVersion = parsed.PatchVersion;
                release.IsPrerelease = parsed.IsPrerelease;
                updated++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Backfilled version fields for {Count} releases", updated);
        }

        return updated;
    }
}
