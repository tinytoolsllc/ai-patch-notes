using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchNotes.Data.SeedData;

namespace PatchNotes.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(PatchNotesDbContext context)
    {
        if (await context.Packages.AnyAsync())
        {
            return; // Already seeded
        }

        var now = DateTimeOffset.UtcNow;
        var packages = await LoadSeedDataAsync(now);

        context.Packages.AddRange(packages);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the package catalog from the embedded packages.json without sample releases.
    /// Skips packages that already exist (by owner/repo). Does not set LastFetchedAt,
    /// so a subsequent sync will fetch all releases from GitHub.
    /// </summary>
    /// <returns>Number of new packages added.</returns>
    public static async Task<int> SeedPackageCatalogAsync(PatchNotesDbContext context)
    {
        var seedPackages = await LoadSeedPackagesAsync();

        var existing = await context.Packages
            .Select(p => new { p.GithubOwner, p.GithubRepo })
            .ToListAsync();

        var existingSet = existing
            .Select(p => $"{p.GithubOwner}/{p.GithubRepo}".ToLowerInvariant())
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var sp in seedPackages)
        {
            var key = $"{sp.GithubOwner}/{sp.GithubRepo}".ToLowerInvariant();
            if (existingSet.Contains(key))
                continue;

            context.Packages.Add(new Package
            {
                Name = sp.Name,
                Url = sp.Url,
                NpmName = sp.NpmName,
                GithubOwner = sp.GithubOwner,
                GithubRepo = sp.GithubRepo,
                CreatedAt = now,
                // LastFetchedAt intentionally null — sync will fetch all releases
            });
            added++;
        }

        if (added > 0)
            await context.SaveChangesAsync();

        return added;
    }

    private static async Task<List<SeedPackage>> LoadSeedPackagesAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "PatchNotes.Data.SeedData.packages.json";

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Could not find embedded resource: {resourceName}");

        return await JsonSerializer.DeserializeAsync<List<SeedPackage>>(stream) ?? [];
    }

    private static async Task<List<Package>> LoadSeedDataAsync(DateTimeOffset now)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "PatchNotes.Data.SeedData.packages.json";

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Could not find embedded resource: {resourceName}");
        }

        var seedPackages = await JsonSerializer.DeserializeAsync<List<SeedPackage>>(stream);
        if (seedPackages == null)
        {
            return [];
        }

        return seedPackages.Select(sp =>
        {
            var package = new Package
            {
                Name = sp.Name,
                Url = sp.Url,
                NpmName = sp.NpmName,
                GithubOwner = sp.GithubOwner,
                GithubRepo = sp.GithubRepo,
                CreatedAt = now,
                LastFetchedAt = now,
            };
            package.Releases = sp.Releases.Select(sr => new Release
            {
                PackageId = package.Id,
                Tag = sr.Tag,
                Title = sr.Title,
                Body = sr.Body,
                PublishedAt = sr.PublishedAt,
                FetchedAt = now
            }).ToList();
            return package;
        }).ToList();
    }
}
