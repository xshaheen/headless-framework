// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SitemapModelTests : TestBase
{
    #region SitemapUrl Tests

    [Fact]
    public void should_snapshot_enumerable_inputs()
    {
        var alternates = new List<SitemapAlternateUrl>
        {
            new() { Location = new Uri("https://www.example.com/en/page"), LanguageCode = "en" },
        };
        var images = new List<SitemapImage> { new(new Uri("https://www.example.com/image.jpg")) };
        var languageCodes = new List<string> { "en" };

        var sitemapUrl = new SitemapUrl(
            alternates,
            new() { Images = images, WriteAlternateLanguageCodes = languageCodes }
        );
        alternates.Clear();
        images.Clear();
        languageCodes.Clear();

        sitemapUrl.AlternateLocations.Should().ContainSingle();
        sitemapUrl.Images.Should().ContainSingle();
        sitemapUrl.WriteAlternateLanguageCodes.Should().ContainSingle();
    }

    [Fact]
    public void should_reject_null_location()
    {
        var act = () => new SitemapUrl((Uri)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_reject_null_alternate_locations()
    {
        var act = () => new SitemapUrl((IEnumerable<SitemapAlternateUrl>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void should_reject_priority_outside_range(float priority)
    {
        var act = () => new SitemapUrl(new Uri("https://www.example.com"), new() { Priority = priority });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void should_accept_priority_within_range(float priority)
    {
        var act = () => new SitemapUrl(new Uri("https://www.example.com"), new() { Priority = priority });

        act.Should().NotThrow();
    }

    #endregion

    #region Absolute location

    // A relative Uri throws from AbsoluteUri only when the writer reaches it, after the XML header and earlier
    // entries are already in the output stream; every location must be rejected at construction instead.
    private static readonly Uri _RelativeUri = new("/products/1", UriKind.Relative);

    [Fact]
    public void should_reject_relative_sitemap_url_location()
    {
        var act = () => new SitemapUrl(_RelativeUri);

        act.Should().Throw<ArgumentException>().WithParameterName("location");
    }

    [Fact]
    public void should_reject_relative_alternate_url_location()
    {
        var act = () => new SitemapAlternateUrl { Location = _RelativeUri, LanguageCode = "en" };

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void should_reject_relative_image_location()
    {
        var act = () => new SitemapImage(_RelativeUri);

        act.Should().Throw<ArgumentException>().WithParameterName("location");
    }

    [Fact]
    public void should_reject_relative_sitemap_reference_location()
    {
        var act = () => new SitemapReference { Location = _RelativeUri };

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    #endregion
}
