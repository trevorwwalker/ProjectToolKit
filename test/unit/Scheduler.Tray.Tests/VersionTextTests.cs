// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 967-B: the number a deploy names in the log window is the build's <c>Major.Minor.Rev</c> (VER-2),
/// not the source sha Windows carries after it nor the fourth number a file version grows.
/// </summary>
public class VersionTextTests
{
    [Theory]
    [InlineData("3.0.8", "3.0.8.0", "3.0.8")]
    [InlineData("3.0.8+abc123", "3.0.8.0", "3.0.8")]
    [InlineData("", "3.0.8.0", "3.0.8")]
    [InlineData(null, "3.10.12.1", "3.10.12.1")]
    [InlineData("3.0.0", "", "3.0.0")]
    [InlineData(null, null, "")]
    [InlineData("not a version", "also not", "")]
    public void The_shown_version_is_major_minor_rev(string? product, string? file, string expected) =>
        Assert.Equal(expected, VersionText.From(product, file));

    [Theory]
    [InlineData("3.0.8+abcdef", "3.0.8")]
    [InlineData(" 3.0.8 ", "3.0.8")]
    [InlineData("3.0.8.0", "3.0.8")]
    [InlineData("", "")]
    public void A_sha_or_a_fourth_number_is_not_part_of_it(string? given, string expected) =>
        Assert.Equal(expected, VersionText.Trim(given));
}
