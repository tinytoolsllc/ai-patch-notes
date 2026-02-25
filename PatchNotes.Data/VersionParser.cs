using System.Text.RegularExpressions;

namespace PatchNotes.Data;

/// <summary>
/// Represents a parsed semantic version with all components.
/// </summary>
public record ParsedVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease,
    string? BuildMetadata,
    string OriginalTag,
    string? MonorepoPackage = null)
{
    /// <summary>
    /// Returns true if this is a pre-release version (has pre-release identifier).
    /// </summary>
    public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);
}

/// <summary>
/// Result of parsing a version tag.
/// </summary>
public record VersionParseResult
{
    public bool Success { get; init; }
    public ParsedVersion? Version { get; init; }
    public string? Error { get; init; }
    public string OriginalTag { get; init; } = string.Empty;

    public static VersionParseResult Ok(ParsedVersion version) =>
        new() { Success = true, Version = version, OriginalTag = version.OriginalTag };

    public static VersionParseResult Fail(string tag, string error) =>
        new() { Success = false, Error = error, OriginalTag = tag };
}

/// <summary>
/// Represents the denormalized version fields extracted from a tag for storage.
/// Returns MajorVersion = -1 when the tag cannot be parsed (unversioned).
/// </summary>
public record ParsedTagValues(int MajorVersion, int MinorVersion, int PatchVersion, bool IsPrerelease);

/// <summary>
/// Consolidated parser for semantic versions from release tags.
/// Supports standard semver, monorepo, release-style, simple (MAJOR.MINOR),
/// and non-standard prerelease formats.
/// </summary>
public static class VersionParser
{
    // Non-standard prerelease: 1.0.0beta1, 1.0.0.rc1
    private static readonly Regex NonStandardPrereleaseRegex = new(
        @"^v?(\d+)\.(\d+)\.(\d+)[.\s]?((?:alpha|beta|canary|rc|next|nightly|dev|preview)\d*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Monorepo: @scope/package@v?MAJOR.MINOR[.PATCH][-PRE][+BUILD]
    private static readonly Regex MonorepoRegex = new(
        @"^(@?[\w-]+(?:/[\w-]+)*)[@/]v?(\d+)\.(\d+)(?:\.(\d+))?(?:-([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?(?:\+([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?$",
        RegexOptions.Compiled);

    // Release-style: release[-/]v?MAJOR.MINOR[.PATCH][-PRE]
    private static readonly Regex ReleaseTagRegex = new(
        @"^release[-/]v?(\d+)\.(\d+)(?:\.(\d+))?(?:-([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Dash-separated monorepo: {package-name}-vMAJOR.MINOR[.PATCH][-PRE][+BUILD]
    // Handles tags like puppeteer-core-v24.37.5, browsers-v2.13.0
    // Greedy match on package ensures longest prefix (puppeteer-core, not puppeteer)
    private static readonly Regex DashMonorepoRegex = new(
        @"^(.+)-v(\d+)\.(\d+)(?:\.(\d+))?(?:-([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?(?:\+([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?$",
        RegexOptions.Compiled);

    // Standard semver: v?MAJOR.MINOR.PATCH[-PRE][+BUILD]
    private static readonly Regex SemverRegex = new(
        @"^v?(\d+)\.(\d+)\.(\d+)(?:-([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?(?:\+([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?$",
        RegexOptions.Compiled);

    // Simple semver: v?MAJOR.MINOR[-PRE]
    private static readonly Regex SimpleSemverRegex = new(
        @"^v?(\d+)\.(\d+)(?:-([a-zA-Z0-9]+(?:\.[a-zA-Z0-9]+)*))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a release tag into a semantic version.
    /// </summary>
    public static VersionParseResult Parse(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return VersionParseResult.Fail(tag, "Tag is empty or whitespace");

        tag = tag.Trim();

        // Try non-standard prerelease first (e.g., "1.0.0beta1", "1.0.0.rc1")
        var nonStd = NonStandardPrereleaseRegex.Match(tag);
        if (nonStd.Success)
        {
            return ParseNonStandardPrereleaseMatch(nonStd, tag);
        }

        // Try monorepo format (most specific after non-standard)
        var monorepoMatch = MonorepoRegex.Match(tag);
        if (monorepoMatch.Success)
        {
            return ParseMonorepoMatch(monorepoMatch, tag);
        }

        // Try release-style tags
        var releaseMatch = ReleaseTagRegex.Match(tag);
        if (releaseMatch.Success)
        {
            return ParseReleaseMatch(releaseMatch, tag);
        }

        // Try dash-separated monorepo format (e.g., puppeteer-core-v24.37.5)
        var dashMonorepoMatch = DashMonorepoRegex.Match(tag);
        if (dashMonorepoMatch.Success)
        {
            return ParseDashMonorepoMatch(dashMonorepoMatch, tag);
        }

        // Try standard semver
        var semverMatch = SemverRegex.Match(tag);
        if (semverMatch.Success)
        {
            return ParseSemverMatch(semverMatch, tag);
        }

        // Try simple semver (MAJOR.MINOR only)
        var simpleMatch = SimpleSemverRegex.Match(tag);
        if (simpleMatch.Success)
        {
            return ParseSimpleSemverMatch(simpleMatch, tag);
        }

        return VersionParseResult.Fail(tag, "Tag does not match any known version format");
    }

    /// <summary>
    /// Parses a tag into denormalized version fields for storage.
    /// Returns -1 for MajorVersion when the tag cannot be parsed (unversioned).
    /// </summary>
    public static ParsedTagValues ParseTagValues(string tag)
    {
        var result = Parse(tag);
        if (result.Success)
            return new ParsedTagValues(result.Version!.Major, result.Version.Minor, result.Version.Patch, result.Version.IsPrerelease);
        return new ParsedTagValues(-1, 0, 0, false);
    }

    private static VersionParseResult ParseNonStandardPrereleaseMatch(Match match, string tag)
    {
        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return VersionParseResult.Fail(tag, "Failed to parse version numbers");
        }

        var prerelease = match.Groups[4].Value;
        return VersionParseResult.Ok(new ParsedVersion(
            major, minor, patch, prerelease, null, tag));
    }

    private static VersionParseResult ParseMonorepoMatch(Match match, string tag)
    {
        var package = match.Groups[1].Value;
        if (!int.TryParse(match.Groups[2].Value, out var major) ||
            !int.TryParse(match.Groups[3].Value, out var minor))
        {
            return VersionParseResult.Fail(tag, "Failed to parse version numbers");
        }

        var patch = 0;
        if (match.Groups[4].Success)
            int.TryParse(match.Groups[4].Value, out patch);

        var prerelease = match.Groups[5].Success ? match.Groups[5].Value : null;
        var build = match.Groups[6].Success ? match.Groups[6].Value : null;

        return VersionParseResult.Ok(new ParsedVersion(
            major, minor, patch, prerelease, build, tag, package));
    }

    private static VersionParseResult ParseDashMonorepoMatch(Match match, string tag)
    {
        var package = match.Groups[1].Value;
        if (!int.TryParse(match.Groups[2].Value, out var major) ||
            !int.TryParse(match.Groups[3].Value, out var minor))
        {
            return VersionParseResult.Fail(tag, "Failed to parse version numbers");
        }

        var patch = 0;
        if (match.Groups[4].Success)
            int.TryParse(match.Groups[4].Value, out patch);

        var prerelease = match.Groups[5].Success ? match.Groups[5].Value : null;
        var build = match.Groups[6].Success ? match.Groups[6].Value : null;

        return VersionParseResult.Ok(new ParsedVersion(
            major, minor, patch, prerelease, build, tag, package));
    }

    private static VersionParseResult ParseReleaseMatch(Match match, string tag)
    {
        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor))
        {
            return VersionParseResult.Fail(tag, "Failed to parse version numbers");
        }

        var patch = 0;
        if (match.Groups[3].Success)
            int.TryParse(match.Groups[3].Value, out patch);

        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;

        return VersionParseResult.Ok(new ParsedVersion(
            major, minor, patch, prerelease, null, tag));
    }

    private static VersionParseResult ParseSemverMatch(Match match, string tag)
    {
        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return VersionParseResult.Fail(tag, "Failed to parse version numbers");
        }

        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        var build = match.Groups[5].Success ? match.Groups[5].Value : null;

        return VersionParseResult.Ok(new ParsedVersion(
            major, minor, patch, prerelease, build, tag));
    }

    private static VersionParseResult ParseSimpleSemverMatch(Match match, string tag)
    {
        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor))
        {
            return VersionParseResult.Fail(tag, "Failed to parse version numbers");
        }

        var prerelease = match.Groups[3].Success ? match.Groups[3].Value : null;

        return VersionParseResult.Ok(new ParsedVersion(
            major, minor, 0, prerelease, null, tag));
    }

}
