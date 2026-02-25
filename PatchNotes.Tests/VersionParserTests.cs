using FluentAssertions;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class VersionParserTests
{
    #region Standard Semver Parsing

    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("v1.0.0", 1, 0, 0)]
    [InlineData("15.0.0", 15, 0, 0)]
    [InlineData("v15.0.0", 15, 0, 0)]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("v0.0.1", 0, 0, 1)]
    [InlineData("100.200.300", 100, 200, 300)]
    public void Parse_StandardSemver_ExtractsVersionNumbers(string tag, int major, int minor, int patch)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.Major.Should().Be(major);
        result.Version.Minor.Should().Be(minor);
        result.Version.Patch.Should().Be(patch);
        result.Version.IsPrerelease.Should().BeFalse();
    }

    [Theory]
    [InlineData("v1.0.0-alpha", "alpha")]
    [InlineData("v1.0.0-beta", "beta")]
    [InlineData("v1.0.0-rc.1", "rc.1")]
    [InlineData("v1.0.0-alpha.1", "alpha.1")]
    [InlineData("v16.2.0-canary.3", "canary.3")]
    [InlineData("v5.9.0-beta.2", "beta.2")]
    [InlineData("1.0.0-preview.1", "preview.1")]
    [InlineData("2.0.0-next.5", "next.5")]
    public void Parse_WithPrerelease_ExtractsPrereleaseIdentifier(string tag, string expectedPrerelease)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.Prerelease.Should().Be(expectedPrerelease);
        result.Version.IsPrerelease.Should().BeTrue();
    }

    [Theory]
    [InlineData("v1.0.0+build123", "build123")]
    [InlineData("v1.0.0+20230101", "20230101")]
    [InlineData("v1.0.0-alpha+build", "build")]
    public void Parse_WithBuildMetadata_ExtractsBuildMetadata(string tag, string expectedBuild)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.BuildMetadata.Should().Be(expectedBuild);
    }

    #endregion

    #region Monorepo Tag Parsing

    [Theory]
    [InlineData("@scope/package@1.0.0", "@scope/package", 1, 0, 0)]
    [InlineData("@package/v1.0.0", "@package", 1, 0, 0)]
    [InlineData("@babel/core@7.23.0", "@babel/core", 7, 23, 0)]
    [InlineData("react-dom/v18.2.0", "react-dom", 18, 2, 0)]
    [InlineData("@types/node@20.10.0", "@types/node", 20, 10, 0)]
    public void Parse_MonorepoTag_ExtractsPackageAndVersion(string tag, string expectedPackage, int major, int minor, int patch)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.MonorepoPackage.Should().Be(expectedPackage);
        result.Version.Major.Should().Be(major);
        result.Version.Minor.Should().Be(minor);
        result.Version.Patch.Should().Be(patch);
    }

    [Theory]
    [InlineData("@scope/package@1.0.0-alpha.1", "@scope/package", "alpha.1")]
    [InlineData("@next/mdx@15.0.0-canary.3", "@next/mdx", "canary.3")]
    public void Parse_MonorepoTagWithPrerelease_ExtractsAll(string tag, string expectedPackage, string expectedPrerelease)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.MonorepoPackage.Should().Be(expectedPackage);
        result.Version.Prerelease.Should().Be(expectedPrerelease);
        result.Version.IsPrerelease.Should().BeTrue();
    }

    #endregion

    #region Dash-Separated Monorepo Tag Parsing

    [Theory]
    [InlineData("puppeteer-core-v24.37.5", "puppeteer-core", 24, 37, 5)]
    [InlineData("puppeteer-v24.37.5", "puppeteer", 24, 37, 5)]
    [InlineData("browsers-v2.13.0", "browsers", 2, 13, 0)]
    [InlineData("some-lib-v1.0.0", "some-lib", 1, 0, 0)]
    [InlineData("my-package-v10.0.0", "my-package", 10, 0, 0)]
    public void Parse_DashMonorepoTag_ExtractsPackageAndVersion(string tag, string expectedPackage, int major, int minor, int patch)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue($"Failed to parse dash-monorepo tag: {tag}");
        result.Version!.MonorepoPackage.Should().Be(expectedPackage);
        result.Version.Major.Should().Be(major);
        result.Version.Minor.Should().Be(minor);
        result.Version.Patch.Should().Be(patch);
        result.Version.IsPrerelease.Should().BeFalse();
    }

    [Theory]
    [InlineData("some-lib-v1.0.0-beta.1", "some-lib", "beta.1")]
    [InlineData("puppeteer-v24.37.5-alpha.2", "puppeteer", "alpha.2")]
    public void Parse_DashMonorepoTagWithPrerelease_ExtractsAll(string tag, string expectedPackage, string expectedPrerelease)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue($"Failed to parse dash-monorepo tag: {tag}");
        result.Version!.MonorepoPackage.Should().Be(expectedPackage);
        result.Version.Prerelease.Should().Be(expectedPrerelease);
        result.Version.IsPrerelease.Should().BeTrue();
    }

    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3")]
    public void Parse_StandardSemverTag_HasNoMonorepoPackage(string tag)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.MonorepoPackage.Should().BeNull();
    }

    #endregion

    #region Simple Semver (MAJOR.MINOR only)

    [Theory]
    [InlineData("v5.9", 5, 9, 0)]
    [InlineData("15.0", 15, 0, 0)]
    [InlineData("v1.0", 1, 0, 0)]
    public void Parse_SimpleSemver_ParsesWithZeroPatch(string tag, int major, int minor, int patch)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.Major.Should().Be(major);
        result.Version.Minor.Should().Be(minor);
        result.Version.Patch.Should().Be(patch);
    }

    [Theory]
    [InlineData("v5.9-rc", "rc")]
    [InlineData("15.0-beta", "beta")]
    public void Parse_SimpleSemverWithPrerelease_Parses(string tag, string expectedPrerelease)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.Prerelease.Should().Be(expectedPrerelease);
        result.Version.IsPrerelease.Should().BeTrue();
    }

    #endregion

    #region Release-Style Tags

    [Theory]
    [InlineData("release-1.0.0", 1, 0, 0)]
    [InlineData("release-v1.0.0", 1, 0, 0)]
    [InlineData("release/v2.0.0", 2, 0, 0)]
    [InlineData("RELEASE-v3.0.0", 3, 0, 0)]
    public void Parse_ReleaseStyleTag_Parses(string tag, int major, int minor, int patch)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue();
        result.Version!.Major.Should().Be(major);
        result.Version.Minor.Should().Be(minor);
        result.Version.Patch.Should().Be(patch);
    }

    #endregion

    #region Non-Standard Prerelease Parsing

    [Theory]
    [InlineData("1.0.0beta2", 1, 0, 0, "beta2")]
    [InlineData("1.0.0beta1", 1, 0, 0, "beta1")]
    [InlineData("1.0.0alpha3", 1, 0, 0, "alpha3")]
    [InlineData("1.0.0rc1", 1, 0, 0, "rc1")]
    [InlineData("v2.0.0beta10", 2, 0, 0, "beta10")]
    [InlineData("1.0.0.rc2", 1, 0, 0, "rc2")]
    [InlineData("1.0.0beta", 1, 0, 0, "beta")]
    [InlineData("1.0.0alpha", 1, 0, 0, "alpha")]
    [InlineData("1.0.0preview1", 1, 0, 0, "preview1")]
    [InlineData("1.0.0preview", 1, 0, 0, "preview")]
    public void Parse_NonStandardPrerelease_ExtractsFullPrerelease(string tag, int major, int minor, int patch, string expectedPrerelease)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeTrue($"Failed to parse non-standard prerelease tag: {tag}");
        result.Version!.Major.Should().Be(major);
        result.Version.Minor.Should().Be(minor);
        result.Version.Patch.Should().Be(patch);
        result.Version.Prerelease.Should().Be(expectedPrerelease);
        result.Version.IsPrerelease.Should().BeTrue();
    }

    #endregion

    #region Non-Semver Tags (Should Fail)

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("stable")]
    [InlineData("main")]
    [InlineData("nightly-build")]
    [InlineData("commit-abc123")]
    public void Parse_NonSemverTag_ReturnsFail(string tag)
    {
        var result = VersionParser.Parse(tag);

        result.Success.Should().BeFalse();
        result.Version.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region ParseTagValues

    [Theory]
    [InlineData("v1.0.0", 1, 0, 0, false)]
    [InlineData("v16.2.0-canary.3", 16, 2, 0, true)]
    [InlineData("@babel/core@7.23.0", 7, 23, 0, false)]
    public void ParseTagValues_ValidTag_ReturnsDenormalized(string tag, int major, int minor, int patch, bool isPrerelease)
    {
        var result = VersionParser.ParseTagValues(tag);

        result.MajorVersion.Should().Be(major);
        result.MinorVersion.Should().Be(minor);
        result.PatchVersion.Should().Be(patch);
        result.IsPrerelease.Should().Be(isPrerelease);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("nightly")]
    [InlineData("")]
    public void ParseTagValues_InvalidTag_ReturnsNegativeOne(string tag)
    {
        var result = VersionParser.ParseTagValues(tag);

        result.MajorVersion.Should().Be(-1);
    }

    #endregion

    #region Real-World Scenarios from Seed Data

    [Fact]
    public void Parse_TypeScriptVersions_ParsesCorrectly()
    {
        var tags = new[] { "v5.9.3", "v5.9.2", "v5.9-rc" };

        foreach (var tag in tags)
        {
            var result = VersionParser.Parse(tag);
            result.Success.Should().BeTrue($"Failed to parse TypeScript tag: {tag}");
        }

        VersionParser.Parse("v5.9-rc").Version!.IsPrerelease.Should().BeTrue();
        VersionParser.Parse("v5.9.3").Version!.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public void Parse_NextJsVersions_ParsesCorrectly()
    {
        var tags = new[] { "v16.2.0-canary.3", "v16.2.0-canary.2", "v16.2.0-canary.1" };

        foreach (var tag in tags)
        {
            var result = VersionParser.Parse(tag);
            result.Success.Should().BeTrue($"Failed to parse Next.js tag: {tag}");
            result.Version!.IsPrerelease.Should().BeTrue();
            result.Version.Prerelease.Should().StartWith("canary");
        }
    }

    [Fact]
    public void Parse_NodeJsVersions_ParsesCorrectly()
    {
        var tags = new[] { "v25.4.0", "v25.3.0", "v24.13.0" };

        foreach (var tag in tags)
        {
            var result = VersionParser.Parse(tag);
            result.Success.Should().BeTrue($"Failed to parse Node.js tag: {tag}");
        }

        VersionParser.Parse("v25.4.0").Version!.Major.Should().Be(25);
        VersionParser.Parse("v24.13.0").Version!.Major.Should().Be(24);
    }

    [Fact]
    public void Parse_ReactVersions_ParsesCorrectly()
    {
        var tags = new[] { "v19.1.4", "v19.0.3" };

        foreach (var tag in tags)
        {
            var result = VersionParser.Parse(tag);
            result.Success.Should().BeTrue($"Failed to parse React tag: {tag}");
            result.Version!.Major.Should().Be(19);
        }
    }

    #endregion
}
