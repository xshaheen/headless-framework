// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentValidation;
using Headless.Api.Identity.Resources;
using Headless.Api.Resources;
using Headless.Api.ServiceDefaults;
using Headless.Testing.Tests;
using Humanizer;

namespace Tests.Localization;

/// <summary>
/// Every package that ships formal Arabic (<c>ar</c>) also ships Egyptian Arabic (<c>ar-EG</c>) and Saudi Arabic
/// (<c>ar-SA</c>) with the same keys and placeholders, so a regional request never mixes registers or prints a raw
/// placeholder.
/// </summary>
public sealed partial class ArabicRegionalResourcesTests : TestBase
{
    private static readonly CultureInfo _EgyptianArabic = CultureInfo.GetCultureInfo("ar-EG");
    private static readonly CultureInfo _SaudiArabic = CultureInfo.GetCultureInfo("ar-SA");
    private static readonly CultureInfo _Arabic = CultureInfo.GetCultureInfo("ar");

    private static readonly string[] _RegionalCultures = ["ar-EG", "ar-SA"];

    public static TheoryData<string> ArabicResourceFiles => [.. _ArabicResourceFiles()];

    public static TheoryData<string, string> RegionalResourceFiles =>
        [.. _ArabicResourceFiles().SelectMany(file => _RegionalCultures.Select(culture => (file, culture)))];

    // The packages this project references that ship Arabic resources; each must deploy every regional satellite.
    public static TheoryData<string, string> ReferencedLocalizedAssemblies =>
        [
            .. new[]
            {
                "Headless.Api",
                "Headless.Api.Identity",
                "Headless.FluentValidation",
                "Headless.MultiTenancy",
            }.SelectMany(assembly => _RegionalCultures.Select(culture => (assembly, culture))),
        ];

    [Fact]
    public void should_find_the_arabic_resources()
    {
        ArabicResourceFiles.Should().NotBeEmpty("the scan must reach src/, or the parity theory checks nothing");
    }

    [Theory]
    [MemberData(nameof(RegionalResourceFiles))]
    public void should_ship_a_regional_variant_with_the_same_keys_and_placeholders(string arabicFile, string culture)
    {
        // given
        var arabicPath = Path.Combine(_RepositoryRoot(), arabicFile);
        var regionalPath = arabicPath[..^".ar.resx".Length] + $".{culture}.resx";

        // when
        File.Exists(regionalPath).Should().BeTrue($"{arabicFile} needs an {culture} sibling");
        var arabic = _ReadValues(arabicPath);
        var regional = _ReadValues(regionalPath);

        // then
        regional.Keys.Should().BeEquivalentTo(arabic.Keys);

        foreach (var (key, arabicValue) in arabic)
        {
            var regionalValue = regional[key];
            regionalValue.Should().NotBeNullOrWhiteSpace($"{key} needs {culture} text");
            _Placeholders(regionalValue)
                .Should()
                .BeEquivalentTo(_Placeholders(arabicValue), $"{key} must fill the same placeholders as formal Arabic");
        }
    }

    [Theory]
    [MemberData(nameof(ReferencedLocalizedAssemblies))]
    public void should_deploy_a_regional_satellite_assembly(string assemblyName, string culture)
    {
        // when
        var satellite = Assembly.Load(assemblyName).GetSatelliteAssembly(CultureInfo.GetCultureInfo(culture));

        // then
        satellite.GetName().CultureName.Should().Be(culture);
        satellite.GetManifestResourceNames().Should().NotBeEmpty();
    }

    [Fact]
    public void should_resolve_each_register_under_its_own_culture()
    {
        // when
        var egyptian = _InCulture(_EgyptianArabic, GeneralMessageDescriber.RateLimitExceeded);
        var saudi = _InCulture(_SaudiArabic, GeneralMessageDescriber.RateLimitExceeded);
        var arabic = _InCulture(_Arabic, GeneralMessageDescriber.RateLimitExceeded);
        var emirati = _InCulture(CultureInfo.GetCultureInfo("ar-AE"), GeneralMessageDescriber.RateLimitExceeded);

        // then
        egyptian.Code.Should().Be(arabic.Code);
        saudi.Code.Should().Be(arabic.Code);
        egyptian.Description.Should().Be("عدد الطلبات كتير. جرّب تاني بعد شوية.");
        saudi.Description.Should().Be("عدد الطلبات كبير. حاول مرة ثانية بعد قليل.");
        arabic.Description.Should().Be("عدد الطلبات كبير جدًا. حاول مرة أخرى لاحقًا.");
        emirati.Description.Should().Be(arabic.Description, "an Arabic region without its own file falls back to ar");
    }

    [Theory]
    [InlineData("ar-EG", "ينفع تطلب استعادة كلمة السر")]
    [InlineData("ar-SA", "يمكنك طلب استعادة كلمة المرور")]
    public void should_humanize_throttling_durations_in_arabic_under_a_regional_culture(string culture, string prefix)
    {
        // given
        var cooldown = TimeSpan.FromMinutes(10);
        var remaining = TimeSpan.FromMinutes(5);

        // when
        var descriptor = _InCulture(
            CultureInfo.GetCultureInfo(culture),
            () => IdentityMessageDescriber.Passwords.RequestForgetCooldown(remaining, cooldown)
        );

        // then: Humanizer falls back from the region to the Arabic resources the package ships
        descriptor.Description.Should().StartWith(prefix);
        descriptor
            .Description.Should()
            .Contain(remaining.Humanize(culture: _Arabic))
            .And.Contain(cooldown.Humanize(culture: _Arabic));
        descriptor.Description.Should().NotContainAny("minute", "Minute");
    }

    [Fact]
    public void should_add_regional_validation_messages_when_configuring_global_settings()
    {
        // when: no reset, since SetupApiTests toggles the same process-wide guard in parallel; the first call in the
        // process ran the full body, so the translations are in place either way
        SetupApi.ConfigureGlobalSettings();

        // then
        var manager = ValidatorOptions.Global.LanguageManager;
        manager.GetString("NotEmptyValidator", _EgyptianArabic).Should().Be("الحقل ده مطلوب.");
        manager.GetString("NotEmptyValidator", _SaudiArabic).Should().Be("هذا الحقل مطلوب.");
        manager.GetString("NotEmptyValidator", _Arabic).Should().Contain("{PropertyName}");
    }

    private static IEnumerable<string> _ArabicResourceFiles()
    {
        return Directory
            .EnumerateFiles(Path.Combine(_RepositoryRoot(), "src"), "*.ar.resx", SearchOption.AllDirectories)
            .Where(path => !_IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(_RepositoryRoot(), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> _ReadValues(string path)
    {
        return XDocument
            .Load(path)
            .Root!.Elements("data")
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => (string?)data.Element("value") ?? "",
                StringComparer.Ordinal
            );
    }

    private static List<string> _Placeholders(string value)
    {
        return [.. _PlaceholderRegex.Matches(value).Select(match => match.Value).Order(StringComparer.Ordinal)];
    }

    private static T _InCulture<T>(CultureInfo culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = culture;

            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static bool _IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;

        return path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
    }

    private static string _RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.None, 100)]
    private static partial Regex _PlaceholderRegex { get; }
}
