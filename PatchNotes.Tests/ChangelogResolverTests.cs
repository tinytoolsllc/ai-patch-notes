using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PatchNotes.Sync.Core.GitHub;
using PatchNotes.Sync.Core;

namespace PatchNotes.Tests;

public class ChangelogResolverTests
{
    private readonly Mock<IGitHubClient> _mockGitHub;
    private readonly Mock<ILogger<ChangelogResolver>> _mockLogger;
    private readonly ChangelogResolver _resolver;

    public ChangelogResolverTests()
    {
        _mockGitHub = new Mock<IGitHubClient>();
        _mockLogger = new Mock<ILogger<ChangelogResolver>>();
        _resolver = new ChangelogResolver(_mockGitHub.Object, _mockLogger.Object);
    }

    #region IsChangelogReference Tests

    [Fact]
    public void IsChangelogReference_NullBody_ReturnsFalse()
    {
        ChangelogResolver.IsChangelogReference(null).Should().BeFalse();
    }

    [Fact]
    public void IsChangelogReference_EmptyBody_ReturnsFalse()
    {
        ChangelogResolver.IsChangelogReference("").Should().BeFalse();
    }

    [Fact]
    public void IsChangelogReference_WhitespaceBody_ReturnsFalse()
    {
        ChangelogResolver.IsChangelogReference("   ").Should().BeFalse();
    }

    [Theory]
    [InlineData("Please refer to CHANGELOG.md for details")]
    [InlineData("See CHANGELOG.md for full details")]
    [InlineData("See HISTORY.md for details")]
    [InlineData("See CHANGES.md for details")]
    [InlineData("Full changelog: https://github.com/owner/repo/compare/v1.0.0...v2.0.0")]
    public void IsChangelogReference_ShortReferenceBody_ReturnsTrue(string body)
    {
        ChangelogResolver.IsChangelogReference(body).Should().BeTrue();
    }

    [Fact]
    public void IsChangelogReference_LongBody_ReturnsFalse()
    {
        var body = new string('x', 300);
        ChangelogResolver.IsChangelogReference(body).Should().BeFalse();
    }

    [Fact]
    public void IsChangelogReference_ShortButNoReference_ReturnsFalse()
    {
        ChangelogResolver.IsChangelogReference("Bug fixes and improvements").Should().BeFalse();
    }

    [Fact]
    public void IsChangelogReference_VeryLongBodyWithChangelogMention_ReturnsFalse()
    {
        // Body is over 300 chars, even though it mentions CHANGELOG.md
        var body = "This release includes many improvements. " + new string('x', 260) + " See CHANGELOG.md";
        ChangelogResolver.IsChangelogReference(body).Should().BeFalse();
    }

    [Theory]
    [InlineData("Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v7.3.1/packages/vite/CHANGELOG.md) for details.")]
    [InlineData("Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v6.0.0-beta.1/packages/vite/CHANGELOG.md) for details.")]
    [InlineData("\ud83d\udd17 [Changelog](https://github.com/prettier/prettier/blob/main/CHANGELOG.md#380)")]
    public void IsChangelogReference_MonorepoAndLongUrlReferences_ReturnsTrue(string body)
    {
        ChangelogResolver.IsChangelogReference(body).Should().BeTrue();
    }

    [Fact]
    public void IsChangelogReference_MarkdownLinkWithChangelogTitle_ReturnsTrue()
    {
        var body = "See [Release Notes](https://example.com/notes) for details.";
        ChangelogResolver.IsChangelogReference(body).Should().BeTrue();
    }

    [Fact]
    public void IsChangelogReference_BareUrlWithChangelogPath_ReturnsTrue()
    {
        var body = "Details: https://example.com/changelog";
        ChangelogResolver.IsChangelogReference(body).Should().BeTrue();
    }

    [Theory]
    [InlineData("[Technical notes](https://example.com/docs)")]
    [InlineData("[Migration notes](https://example.com/migrate)")]
    [InlineData("[Implementation notes](https://foo.com/internal)")]
    public void IsChangelogReference_GenericNotesLink_ReturnsFalse(string body)
    {
        ChangelogResolver.IsChangelogReference(body).Should().BeFalse();
    }

    [Theory]
    [InlineData("See the migration guide for details on breaking changes")]
    [InlineData("See our docs for full details")]
    [InlineData("See above for details")]
    public void IsChangelogReference_GenericSeeForDetails_ReturnsFalse(string body)
    {
        ChangelogResolver.IsChangelogReference(body).Should().BeFalse();
    }

    [Theory]
    [InlineData("Details: https://example.com/CHANGES.md")]
    [InlineData("Details: https://example.com/history.md")]
    [InlineData("Details: https://example.com/release-notes")]
    public void IsChangelogReference_BareUrlWithSpecificKeywords_ReturnsTrue(string body)
    {
        ChangelogResolver.IsChangelogReference(body).Should().BeTrue();
    }

    [Theory]
    [InlineData("Compare: https://github.com/owner/repo/compare/v1...v2#changes")]
    [InlineData("See: https://example.com/commit-history")]
    [InlineData("Link: https://github.com/owner/repo/wiki/Breaking-Changes")]
    [InlineData("Diff: https://example.com/view-changes")]
    public void IsChangelogReference_BroadUrlKeywords_ReturnsFalse(string body)
    {
        ChangelogResolver.IsChangelogReference(body).Should().BeFalse();
    }

    [Fact]
    public void IsChangelogReference_ReleaseNotesLink_ReturnsTrue()
    {
        var body = "See [Release Notes](https://example.com/releases) for details.";
        ChangelogResolver.IsChangelogReference(body).Should().BeTrue();
    }

    #endregion

    #region ExtractPathFromBody Tests

    [Theory]
    [InlineData(
        "Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v7.3.1/packages/vite/CHANGELOG.md) for details.",
        "packages/vite/CHANGELOG.md")]
    [InlineData(
        "\ud83d\udd17 [Changelog](https://github.com/prettier/prettier/blob/main/CHANGELOG.md#380)",
        "CHANGELOG.md")]
    public void ExtractPathFromBody_ExtractsCorrectPath(string body, string expectedPath)
    {
        ChangelogResolver.ExtractPathFromBody(body).Should().Be(expectedPath);
    }

    [Fact]
    public void ExtractPathFromBody_ReturnsNull_WhenNoGitHubUrl()
    {
        ChangelogResolver.ExtractPathFromBody("See CHANGELOG.md for details").Should().BeNull();
    }

    [Fact]
    public void ExtractPathFromBody_ReturnsNull_WhenNullBody()
    {
        ChangelogResolver.ExtractPathFromBody(null).Should().BeNull();
    }

    #endregion

    #region ExtractVersionSection Tests

    [Fact]
    public void ExtractVersionSection_GivenExactVersion_ReturnsMatchingSection()
    {
        var changelog = """
            # Changelog

            ## 2.0.0

            Breaking changes here.

            ## 1.0.0

            Initial release.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v1.0.0");
        result.Should().Be("Initial release.");
    }

    [Fact]
    public void ExtractVersionSection_GivenBracketedVersion_ReturnsMatchingSection()
    {
        var changelog = """
            # Changelog

            ## [1.2.3]

            Some fixes.

            ## [1.2.2]

            Other fixes.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v1.2.3");
        result.Should().Be("Some fixes.");
    }

    [Fact]
    public void ExtractVersionSection_GivenVersionWithVPrefix_ReturnsMatchingSection()
    {
        var changelog = """
            # Changelog

            ## v1.0.0

            Content here.

            ## v0.9.0

            Old content.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v1.0.0");
        result.Should().Be("Content here.");
    }

    [Fact]
    public void ExtractVersionSection_GivenVersionWithDateSuffix_ReturnsMatchingSection()
    {
        var changelog = """
            # Changelog

            ### 1.2.3 (2024-01-15)

            Dated release notes.

            ### 1.2.2 (2024-01-10)

            Older notes.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "1.2.3");
        result.Should().StartWith("Dated release notes.");
    }

    [Fact]
    public void ExtractVersionSection_ReturnsNull_WhenVersionNotFound()
    {
        var changelog = """
            # Changelog

            ## 1.0.0

            Content.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v2.0.0");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractVersionSection_ReturnsNull_WhenNoHeadings()
    {
        var changelog = "Just some text without any headings.";

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v1.0.0");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractVersionSection_ReturnsNull_WhenSectionEmpty()
    {
        var changelog = """
            ## 1.0.0

            ## 0.9.0

            Content here.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v1.0.0");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractVersionSection_ExtractsToEndOfFile_WhenLastSection()
    {
        var changelog = """
            ## 2.0.0

            First section.

            ## 1.0.0

            Last section content.
            With multiple lines.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v1.0.0");
        result.Should().Contain("Last section content.");
        result.Should().Contain("With multiple lines.");
    }

    [Fact]
    public void ExtractVersionSection_GivenSameLevelHeading_StopsExtraction()
    {
        var changelog = """
            ## 2.0.0

            ### Bug Fixes

            - Fixed something.

            ### Features

            - Added something.

            ## 1.0.0

            Initial.
            """;

        var result = ChangelogResolver.ExtractVersionSection(changelog, "v2.0.0");
        result.Should().Contain("Fixed something.");
        result.Should().Contain("Added something.");
        result.Should().NotContain("Initial.");
    }

    #endregion

    #region ResolveAsync Tests

    [Fact]
    public async Task ResolveAsync_GivenValidChangelogUrl_FetchesAndExtractsSection()
    {
        var changelog = """
            ## 2.0.0

            New features.

            ## 1.0.0

            Initial release.
            """;

        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        var result = await _resolver.ResolveAsync("owner", "repo", "v1.0.0");

        result.Should().Be("Initial release.");
    }

    [Fact]
    public async Task ResolveAsync_UsesUrlPathFromBody_ForMonorepoChangelogs()
    {
        var body = "Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v7.3.1/packages/vite/CHANGELOG.md) for details.";
        var changelog = """
            ## 7.3.1

            Monorepo changelog content.

            ## 7.3.0

            Previous version.
            """;

        _mockGitHub
            .Setup(x => x.GetFileContentAsync("vitejs", "vite", "packages/vite/CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        var result = await _resolver.ResolveAsync("vitejs", "vite", "v7.3.1", body);

        result.Should().Be("Monorepo changelog content.");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToStandardPaths_WhenUrlPathFails()
    {
        var body = "Please refer to [CHANGELOG.md](https://github.com/owner/repo/blob/v1.0.0/packages/sub/CHANGELOG.md) for details.";
        var changelog = """
            ## 1.0.0

            Found via fallback.
            """;

        // URL path returns null
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "packages/sub/CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        // Fallback to root CHANGELOG.md works
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        var result = await _resolver.ResolveAsync("owner", "repo", "v1.0.0", body);

        result.Should().Be("Found via fallback.");
    }

    [Fact]
    public async Task ResolveAsync_GivenPrimaryPathFails_TriesFallbackPaths()
    {
        var changelog = """
            ## 1.0.0

            Found in CHANGES.md.
            """;

        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGES.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        var result = await _resolver.ResolveAsync("owner", "repo", "v1.0.0");

        result.Should().Be("Found in CHANGES.md.");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNoChangelogFound()
    {
        _mockGitHub
            .Setup(x => x.GetFileContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _resolver.ResolveAsync("owner", "repo", "v1.0.0");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenVersionNotInChangelog()
    {
        var changelog = """
            ## 2.0.0

            Only this version.
            """;

        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        var result = await _resolver.ResolveAsync("owner", "repo", "v1.0.0");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_GivenExceptionThrown_ReturnsNull()
    {
        _mockGitHub
            .Setup(x => x.GetFileContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        var result = await _resolver.ResolveAsync("owner", "repo", "v1.0.0");

        result.Should().BeNull();
    }

    #endregion

    #region IsLikelyConventionalCommitsOnly Tests

    [Theory]
    [InlineData("### Features\n\n* update rolldown ([#123](https://github.com/owner/repo/issues/123))")]
    [InlineData("### Bug Fixes\n\n* fix crash on startup")]
    [InlineData("### BREAKING CHANGES\n\n* removed old API")]
    [InlineData("### Performance Improvements\n\n* faster builds")]
    [InlineData("### Documentation\n\n* updated readme")]
    [InlineData("### Code Refactoring\n\n* cleanup")]
    [InlineData("### Tests\n\n* added unit tests")]
    [InlineData("## What's Changed\n\n* fix by @user")]
    public void IsLikelyConventionalCommitsOnly_ShortConventionalBody_ReturnsTrue(string body)
    {
        ChangelogResolver.IsLikelyConventionalCommitsOnly(body).Should().BeTrue();
    }

    [Fact]
    public void IsLikelyConventionalCommitsOnly_LongBody_ReturnsFalse()
    {
        var body = "### Features\n\n" + new string('x', 500);
        ChangelogResolver.IsLikelyConventionalCommitsOnly(body).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsLikelyConventionalCommitsOnly_NullOrEmpty_ReturnsFalse(string? body)
    {
        ChangelogResolver.IsLikelyConventionalCommitsOnly(body).Should().BeFalse();
    }

    [Fact]
    public void IsLikelyConventionalCommitsOnly_ProseBody_ReturnsFalse()
    {
        var body = "We're thrilled to announce the release of v8.0.0!";
        ChangelogResolver.IsLikelyConventionalCommitsOnly(body).Should().BeFalse();
    }

    #endregion

    #region ExtractGitHubReleaseLink Tests

    [Theory]
    [InlineData("[Release](https://github.com/dotnet/core/releases/tag/v11.0.0-preview.1)", "dotnet", "core", "v11.0.0-preview.1")]
    [InlineData("https://github.com/dotnet/dotnet/releases/tag/v11.0.100-preview.1.26104.118", "dotnet", "dotnet", "v11.0.100-preview.1.26104.118")]
    [InlineData("[What's New](https://github.com/owner/repo/releases/tag/v2.0.0)", "owner", "repo", "v2.0.0")]
    public void ExtractGitHubReleaseLink_GivenValidLinkBody_ExtractsOwnerRepoAndTag(string body, string owner, string repo, string tag)
    {
        var result = ChangelogResolver.ExtractGitHubReleaseLink(body);
        result.Should().NotBeNull();
        result!.Value.owner.Should().Be(owner);
        result!.Value.repo.Should().Be(repo);
        result!.Value.tag.Should().Be(tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bug fixes and improvements")]
    [InlineData("This is a long body with a link https://github.com/owner/repo/releases/tag/v1.0.0 but it has lots of surrounding context that makes it real content not just a redirect")]
    public void ExtractGitHubReleaseLink_ReturnsNull_ForNonLinkBodies(string? body)
    {
        ChangelogResolver.ExtractGitHubReleaseLink(body).Should().BeNull();
    }

    #endregion

    #region FollowReleaseLinksAsync Tests

    [Fact]
    public async Task FollowReleaseLinksAsync_GivenChainedLinks_ReturnsTerminalRelease()
    {
        var body1 = "[Release](https://github.com/dotnet/core/releases/tag/v11.0.0-preview.1)";
        var body2 = "https://github.com/dotnet/dotnet/releases/tag/v11.0.100-preview.1";
        var body3 = "Actual release notes content here with details.";

        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("dotnet", "core", "v11.0.0-preview.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v11.0.0-preview.1", Body = body2 });
        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("dotnet", "dotnet", "v11.0.100-preview.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v11.0.100-preview.1", Body = body3 });

        var result = await _resolver.FollowReleaseLinksAsync(body1);

        result.Should().Be(body3);
    }

    [Fact]
    public async Task FollowReleaseLinksAsync_GivenChainExceedsMaxHops_StopsAndReturnsLastRelease()
    {
        // Create a chain: repo0 → repo1 → repo2 → repo3 → ...
        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("owner", "repo0", "v1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v1.0.0", Body = "https://github.com/owner/repo1/releases/tag/v1.0.0" });
        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("owner", "repo1", "v1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v1.0.0", Body = "https://github.com/owner/repo2/releases/tag/v1.0.0" });
        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("owner", "repo2", "v1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v1.0.0", Body = "https://github.com/owner/repo3/releases/tag/v1.0.0" });
        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("owner", "repo3", "v1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v1.0.0", Body = "https://github.com/owner/repo4/releases/tag/v1.0.0" });

        var result = await _resolver.FollowReleaseLinksAsync(
            "https://github.com/owner/repo0/releases/tag/v1.0.0", maxHops: 3);

        // Should have followed 3 hops (repo0→repo1→repo2→repo3) and stopped
        result.Should().Be("https://github.com/owner/repo3/releases/tag/v1.0.0");
    }

    [Fact]
    public async Task FollowReleaseLinksAsync_GivenCircularLinks_StopsAndReturnsLastRelease()
    {
        var body = "[Release](https://github.com/owner/repo/releases/tag/v1.0.0)";

        _mockGitHub
            .Setup(x => x.GetReleaseByTagAsync("owner", "repo", "v1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatchNotes.Sync.Core.GitHub.Models.GitHubRelease { TagName = "v1.0.0", Body = body });

        var result = await _resolver.FollowReleaseLinksAsync(body);

        // Should stop after detecting the circular reference
        result.Should().Be(body);
    }

    [Fact]
    public async Task FollowReleaseLinksAsync_ReturnsOriginal_WhenNotALink()
    {
        var body = "Real release notes content.";

        var result = await _resolver.FollowReleaseLinksAsync(body);

        result.Should().Be(body);
    }

    #endregion
}
