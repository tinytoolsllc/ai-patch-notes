using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatchNotes.Sync.Core.GitHub;

namespace PatchNotes.Sync.Core;

/// <summary>
/// Detects release bodies that reference external changelogs and fetches the real content.
/// </summary>
public class ChangelogResolver
{
    private readonly IGitHubClient _github;
    private readonly ILogger<ChangelogResolver> _logger;

    private static readonly string[] ChangelogPaths =
    [
        "CHANGELOG.md",
        "CHANGES.md",
        "HISTORY.md",
        "changelog.md",
        "changes.md",
        "history.md",
        "Changelog.md"
    ];

    private static readonly Regex ChangelogReferencePattern = new(
        @"CHANGELOG\.md|HISTORY\.md|CHANGES\.md|Full changelog: https://github\.com/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkdownLinkPattern = new(
        @"\[(?<title>[^\]]+)\]\((?<url>[^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex BareUrlPattern = new(
        @"https?://\S+",
        RegexOptions.Compiled);

    private static readonly string[] ChangelogLinkTitles =
        ["changelog", "changes", "history", "release notes", "release", "what's changed"];

    private static readonly string[] ChangelogUrlKeywords =
        ["changelog", "changes.md", "history.md", "release-notes"];

    private static readonly Regex GitHubBlobUrlPattern = new(
        @"https://github\.com/[^/]+/[^/]+/blob/[^/]+/(?<path>[^#?)]+)",
        RegexOptions.Compiled);

    private static readonly Regex GitHubReleaseUrlPattern = new(
        @"https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases/tag/(?<tag>[^)\s#]+)",
        RegexOptions.Compiled);

    // Matches headings like: ## [1.2.3], ## 1.2.3, # v1.2.3, ### 1.2.3 (2024-01-15)
    private static readonly Regex HeadingPattern = new(
        @"^(#{1,4})\s+\[?v?(?<version>[^\]\s(]+)\]?[^\r\n]*",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public ChangelogResolver(IGitHubClient github, ILogger<ChangelogResolver> logger)
    {
        _github = github;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a release body is essentially just a link to another GitHub release.
    /// </summary>
    public static (string owner, string repo, string tag)? ExtractGitHubReleaseLink(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length >= 300)
            return null;

        var match = GitHubReleaseUrlPattern.Match(body);
        if (!match.Success)
            return null;

        // Only treat as a release link if the body is basically just that link
        // (with optional markdown wrapper or minimal surrounding text)
        var stripped = body.Trim();
        var urlStart = match.Index;
        var urlEnd = match.Index + match.Length;
        var beforeUrl = stripped[..urlStart].TrimEnd('[', ' ', '\n', '\r');
        var afterUrl = stripped[urlEnd..].TrimStart(')', ' ', '\n', '\r');

        if (beforeUrl.Length > 30 || afterUrl.Length > 30)
            return null;

        return (match.Groups["owner"].Value, match.Groups["repo"].Value, match.Groups["tag"].Value);
    }

    /// <summary>
    /// Follows cross-repo GitHub release links up to maxHops times.
    /// Returns the final body if it has real content, otherwise the last body encountered.
    /// </summary>
    public async Task<string?> FollowReleaseLinksAsync(
        string? body,
        int maxHops = 5,
        CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var hop = 0; hop < maxHops; hop++)
        {
            var link = ExtractGitHubReleaseLink(body);
            if (link == null)
                break;

            var key = $"{link.Value.owner}/{link.Value.repo}/{link.Value.tag}";
            if (!visited.Add(key))
            {
                _logger.LogDebug("[{Owner}/{Repo}] Circular release link detected at {Key}", link.Value.owner, link.Value.repo, key);
                break;
            }

            try
            {
                var release = await _github.GetReleaseByTagAsync(
                    link.Value.owner, link.Value.repo, link.Value.tag, cancellationToken);

                if (release == null || string.IsNullOrWhiteSpace(release.Body))
                {
                    _logger.LogDebug("[{Owner}/{Repo}] Release {Key} not found or has empty body", link.Value.owner, link.Value.repo, key);
                    break;
                }

                _logger.LogDebug("[{Owner}/{Repo}] Followed release link to {Key} (hop {Hop})", link.Value.owner, link.Value.repo, key, hop + 1);
                body = release.Body;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Owner}/{Repo}] Failed to follow release link to {Key}", link.Value.owner, link.Value.repo, key);
                break;
            }
        }

        return body;
    }

    /// <summary>
    /// Checks if a release body looks like a changelog reference rather than real content.
    /// </summary>
    public static bool IsChangelogReference(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        // Long bodies are real content, not references
        if (body.Length >= 300)
            return false;

        // Check markdown links for changelog-related titles or URLs
        foreach (Match match in MarkdownLinkPattern.Matches(body))
        {
            var title = match.Groups["title"].Value;
            var url = match.Groups["url"].Value;

            if (ChangelogLinkTitles.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (ChangelogUrlKeywords.Any(k => url.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // Check bare URLs for changelog paths
        foreach (Match match in BareUrlPattern.Matches(body))
        {
            if (ChangelogUrlKeywords.Any(k => match.Value.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // Catch non-URL patterns like "See CHANGELOG.md for details"
        return ChangelogReferencePattern.IsMatch(body);
    }

    /// <summary>
    /// Extracts the file path from a GitHub blob URL in the body, if present.
    /// e.g. https://github.com/vitejs/vite/blob/v7.3.1/packages/vite/CHANGELOG.md → packages/vite/CHANGELOG.md
    /// </summary>
    public static string? ExtractPathFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var match = GitHubBlobUrlPattern.Match(body);
        return match.Success ? match.Groups["path"].Value : null;
    }

    /// <summary>
    /// Attempts to resolve a changelog reference by fetching the actual changelog content.
    /// Returns the extracted section or null if resolution fails.
    /// </summary>
    public async Task<string?> ResolveAsync(
        string owner,
        string repo,
        string tagName,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        // First, try to extract a file path from a GitHub URL in the body
        var urlPath = ExtractPathFromBody(body);
        if (urlPath == null)
        {
            _logger.LogInformation(
                "[{Owner}/{Repo}] ResolveAsync {Tag}: no GitHub blob URL found in body, falling back to standard paths",
                owner, repo, tagName);
        }
        else
        {
            _logger.LogInformation(
                "[{Owner}/{Repo}] ResolveAsync {Tag}: extracted URL path {Path}",
                owner, repo, tagName, urlPath);
        }

        if (urlPath != null)
        {
            try
            {
                var content = await _github.GetFileContentAsync(owner, repo, urlPath, cancellationToken);
                _logger.LogInformation(
                    "[{Owner}/{Repo}] Fetched {Path} for {Tag}: contentLength={Length}",
                    owner, repo, urlPath, tagName, content?.Length ?? 0);

                if (content != null)
                {
                    var section = ExtractVersionSection(content, tagName);
                    _logger.LogInformation(
                        "[{Owner}/{Repo}] ExtractVersionSection from {Path} for tag {Tag}: sectionLength={Length}",
                        owner, repo, urlPath, tagName, section?.Length ?? 0);

                    if (section != null)
                    {
                        return section;
                    }
                    else
                    {
                        // Log the headings we found so we can debug version matching
                        var headings = HeadingPattern.Matches(content);
                        var headingList = string.Join(", ",
                            headings.Cast<Match>().Take(10).Select(m => m.Groups["version"].Value));
                        _logger.LogWarning(
                            "[{Owner}/{Repo}] No version match for tag {Tag} in {Path}. First headings found: [{Headings}]",
                            owner, repo, tagName, urlPath, headingList);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{Owner}/{Repo}] Failed to fetch changelog {Path}",
                    owner, repo, urlPath);
            }
        }

        // Fall back to standard changelog paths
        if (urlPath != null)
        {
            _logger.LogInformation(
                "[{Owner}/{Repo}] URL path {Path} did not yield a result for {Tag}, falling back to standard changelog paths",
                owner, repo, urlPath, tagName);
        }

        foreach (var path in ChangelogPaths)
        {
            try
            {
                var content = await _github.GetFileContentAsync(owner, repo, path, cancellationToken);
                if (content == null)
                {
                    _logger.LogDebug("[{Owner}/{Repo}] Standard path {Path} not found", owner, repo, path);
                    continue;
                }

                _logger.LogInformation(
                    "[{Owner}/{Repo}] Fetched standard path {Path} for {Tag}: contentLength={Length}",
                    owner, repo, path, tagName, content.Length);

                var section = ExtractVersionSection(content, tagName);
                if (section != null)
                {
                    _logger.LogInformation(
                        "[{Owner}/{Repo}] Resolved changelog for {Tag} from {Path}, sectionLength={Length}",
                        owner, repo, tagName, path, section.Length);
                    return section;
                }
                else
                {
                    var headings = HeadingPattern.Matches(content);
                    var headingList = string.Join(", ",
                        headings.Cast<Match>().Take(10).Select(m => m.Groups["version"].Value));
                    _logger.LogInformation(
                        "[{Owner}/{Repo}] No version match for tag {Tag} in standard path {Path}. First headings: [{Headings}]",
                        owner, repo, tagName, path, headingList);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{Owner}/{Repo}] Failed to fetch changelog {Path}",
                    owner, repo, path);
            }
        }

        _logger.LogWarning(
            "[{Owner}/{Repo}] Could not resolve changelog for {Tag} from any path",
            owner, repo, tagName);
        return null;
    }

    /// <summary>
    /// Extracts the section for a specific version from changelog content.
    /// </summary>
    public static string? ExtractVersionSection(string content, string tagName)
    {
        // Normalize the version: strip leading 'v' from tag for matching
        var version = tagName.TrimStart('v');

        var matches = HeadingPattern.Matches(content);
        if (matches.Count == 0)
            return null;

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var headingVersion = match.Groups["version"].Value;

            if (!VersionMatches(headingVersion, version))
                continue;

            var headingLevel = match.Groups[1].Value.Length;
            var sectionStart = match.Index + match.Length;

            // Find the next heading at the same or higher level
            int sectionEnd = content.Length;
            for (int j = i + 1; j < matches.Count; j++)
            {
                var nextLevel = matches[j].Groups[1].Value.Length;
                if (nextLevel <= headingLevel)
                {
                    sectionEnd = matches[j].Index;
                    break;
                }
            }

            var section = content[sectionStart..sectionEnd].Trim();
            return string.IsNullOrEmpty(section) ? null : section;
        }

        return null;
    }

    /// <summary>
    /// Checks if a changelog body looks like auto-generated conventional-commits output
    /// rather than a hand-written release announcement. Used to flag releases for
    /// re-resolution on subsequent sync runs.
    /// </summary>
    public static bool IsLikelyConventionalCommitsOnly(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length >= 500)
            return false;

        var trimmed = body.TrimStart();
        return trimmed.StartsWith("### Features", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### Bug Fixes", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### Performance Improvements", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### BREAKING CHANGES", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### Reverts", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### Documentation", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### Code Refactoring", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("### Tests", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("## What's Changed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool VersionMatches(string headingVersion, string targetVersion)
    {
        // Exact match
        if (string.Equals(headingVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        // Strip leading 'v' from heading version too
        var normalizedHeading = headingVersion.TrimStart('v');
        return string.Equals(normalizedHeading, targetVersion, StringComparison.OrdinalIgnoreCase);
    }
}
