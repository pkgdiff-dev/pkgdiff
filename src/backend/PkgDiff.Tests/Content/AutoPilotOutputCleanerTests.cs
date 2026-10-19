using PkgDiff.Application.Services;

namespace PkgDiff.Tests.Content;

public class AutoPilotOutputCleanerTests
{
    private const string ChalkProductionOutput =
        "# 🚀 🧲️ 🐥 - 🌟🔥 - 🆗️ Release Notes of the 🔎 NEWER 🙄 Version of 🔳 AGENCY 🔮 Terminal String Stylizing 🔼\n\n" +
        "## 🌟 What's New in chalk v6.0.0 🔥\n\n" +
        "- chalk is now faster than ever 🚀\n" +
        "- New amazing color support for every terminal\n\n" +
        "Requirement: Node.js 22.8a94e0e\n\n" +
        "```js\n" +
        "import { ansi256 } from 'chalk';\n" +
        "console.log(chalk.bg('red')('Hello'));\n" +
        "```\n\n" +
        "## 🌟 What's New in chalk v6.0.0 🔥\n\n" +
        "- chalk is now quicker than before 🚀\n" +
        "- Amazing new colour support for all terminals\n\n" +
        "Requirement: Node.js 22.8a94e0e\n\n" +
        "```js\n" +
        "import { ansi256 } from 'chalk';\n" +
        "console.log(chalk.bg('red')('Hello'));\n" +
        "```\n\n" +
        "## 🌟 What's New in chalk v6.0.0 🔥\n\n" +
        "- chalk got a speed boost 🚀\n" +
        "- Colors now work in more terminals\n\n" +
        "Requirement: Node.js 22.8a94e0e\n\n" +
        "```js\n" +
        "import { ansi256 } from 'chalk';\n" +
        "console.log(chalk.bg('red')('Hello'));\n" +
        "```\n";

    private const string ChalkReleaseNotes =
        "Breaking\n" +
        "- Require Node.js 20\n" +
        "- Remove the deprecated chalk.keyword() method\n\n" +
        "Improvements\n" +
        "- Faster color level detection\n\n" +
        "Install with:\n" +
        "npm install chalk@6\n";

    private const string WellFormedPost =
        "## Overview\n\n" +
        "chalk is a library for styling terminal strings with colors. " +
        "This post covers the changes in chalk 6.0.0 as stated in the release notes.\n\n" +
        "## What's changed\n\n" +
        "- Node.js 20 is now required.\n" +
        "- The deprecated chalk.keyword() method was removed.\n" +
        "- Color level detection is faster.\n\n" +
        "## Upgrade notes\n\n" +
        "- Upgrade to Node.js 20 or later before installing chalk 6.\n" +
        "- Replace calls to chalk.keyword() before upgrading.";

    [Fact]
    public void Chalk_production_output_is_rejected()
    {
        var cleaned = AutoPilotOutputCleaner.Clean(ChalkProductionOutput, ChalkReleaseNotes);

        Assert.False(AutoPilotQualityGate.Passes(cleaned, "chalk", out var reason));
        Assert.NotEmpty(reason);
        Assert.DoesNotContain("chalk.bg('red')", cleaned);
        Assert.DoesNotContain("Terminal String Stylizing", cleaned);
    }

    [Fact]
    public void Emoji_are_stripped()
    {
        var input =
            "## 🚀 Overview ✨\n" +
            "- Added 🔥 support\n" +
            "1️⃣ First\n" +
            "Flag 🇹🇷 here\n" +
            "Family 👨‍👩‍👧 here\n" +
            "C# → F#\n" +
            "café\n" +
            "- Parent\n" +
            "  - Nested child";

        var lines = AutoPilotOutputCleaner.Clean(input, string.Empty).Split('\n');

        Assert.Equal("## Overview", lines[0]);
        Assert.Equal("- Added support", lines[1]);
        Assert.Equal("1 First", lines[2]);
        Assert.Equal("Flag here", lines[3]);
        Assert.Equal("Family here", lines[4]);
        Assert.Equal("C# → F#", lines[5]);
        Assert.Equal("café", lines[6]);
        Assert.Equal("  - Nested child", lines[8]);
    }

    [Fact]
    public void H1_lines_are_removed()
    {
        var notes = "Example:\n# comment\nnpm install chalk@6";
        var input =
            "# Big Title\n\n" +
            "## Overview\n\n" +
            "Text.\n\n" +
            "```sh\n" +
            "# comment\n" +
            "npm install chalk@6\n" +
            "```\n\n" +
            "#\n";

        var cleaned = AutoPilotOutputCleaner.Clean(input, notes);
        var lines = cleaned.Split('\n');

        Assert.DoesNotContain(lines, line => line.StartsWith("# Big", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line == "#");
        Assert.Contains("## Overview", lines);
        Assert.Contains("# comment", lines);
    }

    [Theory]
    [InlineData("# What's changed", "## What's changed")]
    [InlineData("## 2. What's New", "## What's changed")]
    [InlineData("## Changes", "## What's changed")]
    [InlineData("**What’s Changed**", "## What's changed")]
    [InlineData("**Upgrade Notes:**", "## Upgrade notes")]
    [InlineData("Summary:", "## Overview")]
    [InlineData("# Introduction", "## Overview")]
    [InlineData("## Migration guide", "## Upgrade notes")]
    [InlineData("### Overview", "## Overview")]
    public void Common_heading_variants_are_canonicalized(string modelHeading, string expected)
    {
        var cleaned = AutoPilotOutputCleaner.Clean(modelHeading + "\n\n- a", string.Empty);

        Assert.Equal(expected, cleaned.Split('\n')[0]);
    }

    [Theory]
    [InlineData("### Changes")]
    [InlineData("#### What's changed")]
    [InlineData("## What's New in chalk v6.0.0")]
    public void Subheadings_and_other_titles_are_not_promoted(string modelHeading)
    {
        var cleaned = AutoPilotOutputCleaner.Clean("## Overview\n\n" + modelHeading + "\n\n- a", string.Empty);

        Assert.DoesNotContain("## What's changed", cleaned.Split('\n'));
    }

    [Fact]
    public void Post_with_small_model_heading_variants_passes_the_gate()
    {
        var modelOutput =
            "# chalk 6.0.0\n\n" +
            "**Overview**\n\n" +
            "chalk is a library for styling terminal strings with colors. " +
            "This post covers the changes in chalk 6.0.0 as stated in the release notes.\n\n" +
            "## What’s New\n\n" +
            "- Node.js 20 is now required.\n" +
            "- The deprecated chalk.keyword() method was removed.\n" +
            "- Color level detection is faster.\n\n" +
            "## 3. Upgrade Notes:\n\n" +
            "- Upgrade to Node.js 20 or later before installing chalk 6.\n" +
            "- Replace calls to chalk.keyword() before upgrading.";

        var cleaned = AutoPilotOutputCleaner.Clean(modelOutput, ChalkReleaseNotes);

        Assert.True(AutoPilotQualityGate.Passes(cleaned, "chalk", out var reason), reason);
        Assert.DoesNotContain("# chalk 6.0.0", cleaned.Split('\n'));
    }

    [Fact]
    public void Required_headings_are_canonicalized()
    {
        var cleaned = AutoPilotOutputCleaner.Clean("### What's Changed:\n\n- a\n\n## What’s changed\n\n- b", string.Empty);

        var lines = cleaned.Split('\n');
        Assert.Equal("## What's changed", lines[0]);
        Assert.Equal("## What's changed", lines[4]);
    }

    [Fact]
    public void Invented_code_block_is_removed_but_code_from_notes_is_kept()
    {
        var input =
            "## Overview\n\n" +
            "Intro.\n\n" +
            "```js\n" +
            "console.log(chalk.bg('red')('Hello'));\n" +
            "```\n\n" +
            "## Upgrade notes\n\n" +
            "```\n" +
            "npm install chalk@6\n" +
            "```\n\n" +
            "Tail.\n\n" +
            "```js\n" +
            "const invented = chalk.rainbow();\n";

        var cleaned = AutoPilotOutputCleaner.Clean(input, ChalkReleaseNotes);

        Assert.DoesNotContain("chalk.bg('red')", cleaned);
        Assert.DoesNotContain("```js", cleaned);
        Assert.Contains("```\nnpm install chalk@6\n```", cleaned);
        Assert.DoesNotContain("chalk.rainbow", cleaned);
        Assert.EndsWith("Tail.", cleaned);
    }

    [Fact]
    public void Exact_duplicate_paragraph_is_removed()
    {
        var input =
            "## What's changed\n\n" +
            "- Faster color detection.\n\n" +
            "## Upgrade notes\n\n" +
            "- Faster color detection.\n\n" +
            "- Node.js 20 is required.";

        var cleaned = AutoPilotOutputCleaner.Clean(input, string.Empty);

        Assert.Equal(1, CountOccurrences(cleaned, "- Faster color detection."));
        Assert.Contains("- Node.js 20 is required.", cleaned);
        Assert.Contains("## Upgrade notes", cleaned);
    }

    [Fact]
    public void Exact_duplicate_section_is_removed_and_post_passes()
    {
        var changed =
            "## What's changed\n\n" +
            "- Node.js 20 is now required.\n" +
            "- The deprecated chalk.keyword() method was removed.\n" +
            "- Color level detection is faster.\n\n";
        var input =
            "## Overview\n\n" +
            "chalk is a library for styling terminal strings with colors. " +
            "This post covers the changes in chalk 6.0.0 as stated in the release notes.\n\n" +
            changed +
            changed +
            "## Upgrade notes\n\n" +
            "- Upgrade to Node.js 20 or later before installing chalk 6.\n" +
            "- Replace calls to chalk.keyword() before upgrading.";

        var cleaned = AutoPilotOutputCleaner.Clean(input, ChalkReleaseNotes);

        Assert.Equal(1, CountOccurrences(cleaned, "## What's changed"));
        Assert.True(AutoPilotQualityGate.Passes(cleaned, "chalk", out var reason), reason);
    }

    [Fact]
    public void Repeated_heading_with_different_body_is_rejected()
    {
        var input = WellFormedPost + "\n\n## What's changed\n\n- Colors got a lot better in every terminal.\n- Speed improved.";

        var cleaned = AutoPilotOutputCleaner.Clean(input, ChalkReleaseNotes);

        Assert.Equal(2, CountOccurrences(cleaned, "## What's changed"));
        Assert.False(AutoPilotQualityGate.Passes(cleaned, "chalk", out var reason));
        Assert.Contains("repetition", reason);
    }

    [Fact]
    public void High_line_repetition_is_rejected()
    {
        var repeated = string.Concat(Enumerable.Repeat("- Color level detection is faster.\n", 8));
        var post =
            "## Overview\n\n" +
            "chalk is a library for styling terminal strings with colors. " +
            "This post covers the changes in chalk 6.0.0 as stated in the release notes.\n\n" +
            "## What's changed\n\n" +
            repeated + "\n" +
            "## Upgrade notes\n\n" +
            "The release notes do not mention breaking changes.";

        Assert.False(AutoPilotQualityGate.Passes(post, "chalk", out var reason));
        Assert.Contains("repetition", reason);
    }

    [Fact]
    public void Well_formed_factual_post_is_accepted()
    {
        var cleaned = AutoPilotOutputCleaner.Clean(WellFormedPost, ChalkReleaseNotes);

        Assert.True(AutoPilotQualityGate.Passes(cleaned, "chalk", out var reason));
        Assert.Equal(string.Empty, reason);
        Assert.Equal(WellFormedPost, cleaned);
    }

    [Fact]
    public void Short_post_rejected()
    {
        var post = "## Overview\n\nchalk.\n\n## What's changed\n\n- x\n\n## Upgrade notes\n\n- y";

        Assert.False(AutoPilotQualityGate.Passes(post, "chalk", out var reason));
        Assert.Contains("too short", reason);
    }

    [Fact]
    public void Missing_upgrade_notes_rejected()
    {
        var post = WellFormedPost.Replace("## Upgrade notes", "## Notes") + new string('.', 50);

        Assert.False(AutoPilotQualityGate.Passes(post, "chalk", out var reason));
        Assert.Contains("## Upgrade notes", reason);
    }

    [Fact]
    public void Missing_package_mention_rejected()
    {
        var post = WellFormedPost.Replace("chalk", "the library");

        Assert.False(AutoPilotQualityGate.Passes(post, "chalk", out var reason));
        Assert.Contains("does not mention the package", reason);
    }

    [Fact]
    public void Scoped_npm_package_accepted_by_last_segment()
    {
        var post = WellFormedPost.Replace("chalk", "widget");

        Assert.True(AutoPilotQualityGate.Passes(post, "@scope/widget", out var reason), reason);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
