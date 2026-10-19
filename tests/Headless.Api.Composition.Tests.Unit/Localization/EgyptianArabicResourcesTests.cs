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
/// Every package that ships formal Arabic (<c>ar</c>) also ships Egyptian Arabic (<c>ar-EG</c>) with the same keys and
/// placeholders, so a request under <c>ar-EG</c> never mixes registers or prints a raw placeholder.
/// </summary>
public sealed partial class EgyptianArabicResourcesTests : TestBase
{
    private static readonly CultureInfo _EgyptianArabic = CultureInfo.GetCultureInfo("ar-EG");
    private static readonly CultureInfo _Arabic = CultureInfo.GetCultureInfo("ar");

    public static TheoryData<string> ArabicResourceFiles =>
        [
            .. Directory
                .EnumerateFiles(Path.Combine(_RepositoryRoot(), "src"), "*.ar.resx", SearchOption.AllDirectories)
                .Where(path => !_IsBuildOutput(path))
                .Select(path => Path.GetRelativePath(_RepositoryRoot(), path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

    // The packages this project references that ship Arabic resources; each must deploy an ar-EG satellite assembly.
    public static TheoryData<string> ReferencedLocalizedAssemblies =>
        ["Headless.Api", "Headless.Api.Identity", "Headless.FluentValidation", "Headless.MultiTenancy"];

    [Fact]
    public void should_find_the_arabic_resources()
    {
        ArabicResourceFiles.Should().NotBeEmpty("the scan must reach src/, or the parity theory checks nothing");
    }

    [Theory]
    [MemberData(nameof(ArabicResourceFiles))]
    public void should_ship_an_egyptian_variant_with_the_same_keys_and_placeholders(string arabicFile)
    {
        // given
        var arabicPath = Path.Combine(_RepositoryRoot(), arabicFile);
        var egyptianPath = arabicPath[..^".ar.resx".Length] + ".ar-EG.resx";

        // when
        File.Exists(egyptianPath).Should().BeTrue($"{arabicFile} needs an ar-EG sibling");
        var arabic = _ReadValues(arabicPath);
        var egyptian = _ReadValues(egyptianPath);

        // then
        egyptian.Keys.Should().BeEquivalentTo(arabic.Keys);

        foreach (var (key, arabicValue) in arabic)
        {
            var egyptianValue = egyptian[key];
            egyptianValue.Should().NotBeNullOrWhiteSpace($"{key} needs Egyptian text");
            _Placeholders(egyptianValue)
                .Should()
                .BeEquivalentTo(_Placeholders(arabicValue), $"{key} must fill the same placeholders as formal Arabic");
        }
    }

    [Theory]
    [MemberData(nameof(ReferencedLocalizedAssemblies))]
    public void should_deploy_an_egyptian_satellite_assembly(string assemblyName)
    {
        // when
        var satellite = Assembly.Load(assemblyName).GetSatelliteAssembly(_EgyptianArabic);

        // then
        satellite.GetName().CultureName.Should().Be("ar-EG");
        satellite.GetManifestResourceNames().Should().NotBeEmpty();
    }

    [Fact]
    public void should_resolve_egyptian_text_under_ar_eg_and_formal_text_under_ar()
    {
        // when
        var egyptian = _InCulture(_EgyptianArabic, GeneralMessageDescriber.RateLimitExceeded);
        var arabic = _InCulture(_Arabic, GeneralMessageDescriber.RateLimitExceeded);
        var gulfArabic = _InCulture(CultureInfo.GetCultureInfo("ar-SA"), GeneralMessageDescriber.RateLimitExceeded);

        // then
        egyptian.Code.Should().Be(arabic.Code);
        egyptian.Description.Should().Be("عدد الطلبات كتير. جرّب تاني بعد شوية.");
        arabic.Description.Should().Be("عدد الطلبات كبير جدًا. حاول مرة أخرى لاحقًا.");
        gulfArabic.Description.Should().Be(arabic.Description, "another Arabic region falls back to formal Arabic");
    }

    [Fact]
    public void should_humanize_throttling_durations_in_arabic_under_ar_eg()
    {
        // given
        var cooldown = TimeSpan.FromMinutes(10);
        var remaining = TimeSpan.FromMinutes(5);

        // when
        var descriptor = _InCulture(
            _EgyptianArabic,
            () => IdentityMessageDescriber.Passwords.RequestForgetCooldown(remaining, cooldown)
        );

        // then: Humanizer falls back from ar-EG to the Arabic resources the package ships
        var arabicRemaining = remaining.Humanize(culture: _Arabic);
        descriptor.Description.Should().StartWith("ينفع تطلب استعادة كلمة السر");
        descriptor.Description.Should().Contain(arabicRemaining).And.Contain(cooldown.Humanize(culture: _Arabic));
        descriptor.Description.Should().NotContainAny("minute", "Minute");
    }

    [Fact]
    public void should_add_egyptian_validation_messages_when_configuring_global_settings()
    {
        // when: no reset, since SetupApiTests toggles the same process-wide guard in parallel; the first call in the
        // process ran the full body, so the translations are in place either way
        SetupApi.ConfigureGlobalSettings();

        // then
        var manager = ValidatorOptions.Global.LanguageManager;
        manager.GetString("NotEmptyValidator", _EgyptianArabic).Should().Be("الحقل ده مطلوب.");
        manager.GetString("NotEmptyValidator", _Arabic).Should().Contain("{PropertyName}");
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
