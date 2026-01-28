// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SitemapModelTests : TestBase
{
    #region SitemapUrl Tests

    [Fact]
    public void should_store_location_from_constructor()
    {
        var location = new Uri("https://www.example.com/page");

        var sitemapUrl = new SitemapUrl(location);

        sitemapUrl.Location.Should().Be(location);
    }

    [Fact]
    public void should_store_optional_parameters()
    {
        var location = new Uri("https://www.example.com/page");
        var lastModified = new DateTime(2021, 3, 15);
        const ChangeFrequency changeFrequency = ChangeFrequency.Weekly;
        const float priority = 0.8f;

        var sitemapUrl = new SitemapUrl(
            location: location,
            lastModified: lastModified,
            changeFrequency: changeFrequency,
            priority: priority
        );

        sitemapUrl.Location.Should().Be(location);
        sitemapUrl.LastModified.Should().Be(lastModified);
        sitemapUrl.ChangeFrequency.Should().Be(changeFrequency);
        sitemapUrl.Priority.Should().Be(priority);
    }

    [Fact]
    public void should_store_images_collection()
    {
        var location = new Uri("https://www.example.com/page");
        var images = new[]
        {
            new SitemapImage(new Uri("https://www.example.com/image1.jpg")),
            new SitemapImage(new Uri("https://www.example.com/image2.jpg")),
        };

        var sitemapUrl = new SitemapUrl(location: location, images: images);

        sitemapUrl.Images.Should().BeEquivalentTo(images);
    }

    [Fact]
    public void should_store_alternate_locations()
    {
        var alternates = new[]
        {
            new SitemapAlternateUrl { Location = new Uri("https://www.example.com/en/page"), LanguageCode = "en" },
            new SitemapAlternateUrl { Location = new Uri("https://www.example.com/de/page"), LanguageCode = "de" },
        };

        var sitemapUrl = new SitemapUrl(alternateLocations: alternates);

        sitemapUrl.AlternateLocations.Should().BeEquivalentTo(alternates);
        sitemapUrl.Location.Should().BeNull();
    }

    [Fact]
    public void should_allow_null_location_when_using_alternates()
    {
        var alternates = new[]
        {
            new SitemapAlternateUrl { Location = new Uri("https://www.example.com/en/page"), LanguageCode = "en" },
        };

        var sitemapUrl = new SitemapUrl(alternateLocations: alternates);

        sitemapUrl.Location.Should().BeNull();
        sitemapUrl.AlternateLocations.Should().NotBeNull();
    }

    [Fact]
    public void should_store_WriteAlternateLanguageCodes()
    {
        var alternates = new[]
        {
            new SitemapAlternateUrl { Location = new Uri("https://www.example.com/en/page"), LanguageCode = "en" },
        };
        var langCodes = new[] { "en", "de" };

        var sitemapUrl = new SitemapUrl(alternateLocations: alternates, writeAlternateLanguageCodes: langCodes);

        sitemapUrl.WriteAlternateLanguageCodes.Should().BeEquivalentTo(langCodes);
    }

    #endregion

    #region SitemapImage Tests

    [Fact]
    public void should_store_location_uri()
    {
        var location = new Uri("https://www.example.com/image.jpg");

        var image = new SitemapImage(location);

        image.Location.Should().Be(location);
    }

    #endregion

    #region SitemapReference Tests

    [Fact]
    public void should_store_reference_location()
    {
        var location = new Uri("https://www.example.com/sitemap.xml");

        var reference = new SitemapReference { Location = location };

        reference.Location.Should().Be(location);
    }

    [Fact]
    public void should_store_reference_last_modified()
    {
        var location = new Uri("https://www.example.com/sitemap.xml");
        var lastModified = new DateTime(2021, 3, 15);

        var reference = new SitemapReference { Location = location, LastModified = lastModified };

        reference.LastModified.Should().Be(lastModified);
    }

    #endregion

    #region SitemapAlternateUrl Tests

    [Fact]
    public void should_store_alternate_url_properties()
    {
        var location = new Uri("https://www.example.com/en/page");
        const string languageCode = "en-US";

        var alternateUrl = new SitemapAlternateUrl { Location = location, LanguageCode = languageCode };

        alternateUrl.Location.Should().Be(location);
        alternateUrl.LanguageCode.Should().Be(languageCode);
    }

    #endregion

    #region ChangeFrequency Enum Tests

    [Theory]
    [InlineData(ChangeFrequency.Always, "always")]
    [InlineData(ChangeFrequency.Hourly, "hourly")]
    [InlineData(ChangeFrequency.Daily, "daily")]
    [InlineData(ChangeFrequency.Weekly, "weekly")]
    [InlineData(ChangeFrequency.Monthly, "monthly")]
    [InlineData(ChangeFrequency.Yearly, "yearly")]
    [InlineData(ChangeFrequency.Never, "never")]
    public void should_have_correct_changefrequency_values(ChangeFrequency frequency, string expectedLower)
    {
        frequency.ToString().ToLowerInvariant().Should().Be(expectedLower);
    }

    #endregion
}
