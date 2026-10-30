using System.Text.RegularExpressions;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class VersionAndInstallTests
{
    private static string Repo(string file) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", file);

    [Fact]
    public void Terms_carry_a_date_and_the_beta_and_creator_sections()
    {
        // The app re-asks for acceptance when "Last updated" changes, so it must be there.
        var terms = File.ReadAllText(Repo("TERMS.md"));
        Assert.Matches(@"Last updated: \w+ \d{1,2}, \d{4}", terms);
        Assert.Contains("## 2. Beta", terms);
        Assert.Contains("## 3. Creators", terms);
        Assert.Contains("All rights reserved", File.ReadAllText(Repo("LICENSE")));
        Assert.DoesNotContain("Permission is hereby granted, free of charge", File.ReadAllText(Repo("LICENSE")));
    }

    [Fact]
    public void Build_version_is_the_newest_changelog_release()
    {
        var props = File.ReadAllText(Repo("Directory.Build.props"));
        var version = Regex.Match(props, @"<Version>(\d+\.\d+\.\d+)</Version>").Groups[1].Value;
        var newest = Regex.Match(File.ReadAllText(Repo("CHANGELOG.md")), @"^## v?(\d+\.\d+\.\d+)", RegexOptions.Multiline).Groups[1].Value;
        Assert.NotEqual("", newest);
        Assert.Equal(newest, version);
    }

    [Fact]
    public void Travel_scale_matches_the_setup_default_without_a_settings_file()
    {
        // RotaHub installs run no setup, so there may be no community.path or profiles.json.
        Assert.Equal(1.4, CommunityProfile.DefaultTravelScale);
    }
}
