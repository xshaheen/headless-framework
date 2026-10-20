// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Resources;
using Headless.FluentValidation.Resources;
using Headless.Testing.Tests;

namespace Tests;

public sealed partial class HeadlessLanguageManagerExtensionsTests : TestBase
{
    private static readonly CultureInfo _EgyptianArabic = CultureInfo.GetCultureInfo("ar-EG");
    private static readonly CultureInfo _SaudiArabic = CultureInfo.GetCultureInfo("ar-SA");
    private static readonly CultureInfo _Arabic = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo _English = CultureInfo.GetCultureInfo("en");

    // Every key FluentValidation 12 keeps a default message under; StringEnumValidator reads EnumValidator's.
    private static readonly string[] _BuiltInKeys =
    [
        "NotNullValidator",
        "NotEmptyValidator",
        "EmptyValidator",
        "NullValidator",
        "EmailValidator",
        "RegularExpressionValidator",
        "CreditCardValidator",
        "EnumValidator",
        "PredicateValidator",
        "AsyncPredicateValidator",
        "EqualValidator",
        "NotEqualValidator",
        "GreaterThanValidator",
        "GreaterThanOrEqualValidator",
        "LessThanValidator",
        "LessThanOrEqualValidator",
        "InclusiveBetweenValidator",
        "InclusiveBetween_Simple",
        "ExclusiveBetweenValidator",
        "LengthValidator",
        "Length_Simple",
        "MinimumLengthValidator",
        "MinimumLength_Simple",
        "MaximumLengthValidator",
        "MaximumLength_Simple",
        "ExactLengthValidator",
        "ExactLength_Simple",
        "ScalePrecisionValidator",
    ];

    public static TheoryData<string, string> RegionalBuiltInKeys =>
        [.. _BuiltInKeys.SelectMany(key => new[] { ("ar-EG", key), ("ar-SA", key) })];

    [Theory]
    [MemberData(nameof(RegionalBuiltInKeys))]
    public void should_translate_every_built_in_message_for_a_region_and_keep_ar_formal(string culture, string key)
    {
        // given
        var region = CultureInfo.GetCultureInfo(culture);
        var stock = new LanguageManager();
        var manager = new LanguageManager();

        // when
        manager.AddHeadlessEgyptianArabicTranslations();
        manager.AddHeadlessSaudiArabicTranslations();

        // then
        var regional = manager.GetString(key, region);
        regional.Should().NotBeNullOrWhiteSpace();
        regional.Should().NotBe(stock.GetString(key, region), $"{culture} must not fall back to formal Arabic");
        manager.GetString(key, _Arabic).Should().Be(stock.GetString(key, _Arabic));
        manager.GetString(key, _English).Should().Be(stock.GetString(key, _English));

        // The English template defines which placeholders the validator fills; an unknown one would print raw.
        _Placeholders(regional).Should().BeSubsetOf(_Placeholders(stock.GetString(key, _English)));
        _Placeholders(regional).Should().NotContain("{PropertyName}");
    }

    [Fact]
    public void should_report_ar_eg_ar_sa_ar_and_en_messages_when_validating_under_each_culture()
    {
        // given: validators read only the process-wide manager. Adding the regional entries to it is what the API
        // defaults do at startup, and it changes no other culture's lookup.
        var stock = new LanguageManager();
        var global = ValidatorOptions.Global.LanguageManager.Should().BeAssignableTo<LanguageManager>().Subject;
        global.AddHeadlessEgyptianArabicTranslations();
        global.AddHeadlessSaudiArabicTranslations();
        var validator = new NameValidator();

        // when
        var egyptian = _InCulture(_EgyptianArabic, () => validator.Validate(new Person(Name: "")));
        var saudi = _InCulture(_SaudiArabic, () => validator.Validate(new Person(Name: "")));
        var arabic = _InCulture(_Arabic, () => validator.Validate(new Person(Name: "")));
        var english = _InCulture(_English, () => validator.Validate(new Person(Name: "")));

        // then
        egyptian.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("الحقل ده مطلوب.");
        saudi.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("هذا الحقل مطلوب.");
        arabic
            .Errors.Should()
            .ContainSingle()
            .Which.ErrorMessage.Should()
            .Be(
                stock
                    .GetString("NotEmptyValidator", _Arabic)
                    .Replace("{PropertyName}", "Name", StringComparison.Ordinal)
            );
        english.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("'Name' must not be empty.");
    }

    [Fact]
    public void should_let_a_later_translation_replace_the_regional_defaults()
    {
        // given
        var manager = new LanguageManager();
        manager.AddHeadlessEgyptianArabicTranslations();
        manager.AddHeadlessSaudiArabicTranslations();

        // when
        manager.AddTranslation("ar-EG", "NotEmptyValidator", "اكتب قيمة.");
        manager.AddTranslation("ar-SA", "NotEmptyValidator", "أدخل قيمة.");

        // then
        manager.GetString("NotEmptyValidator", _EgyptianArabic).Should().Be("اكتب قيمة.");
        manager.GetString("NotEmptyValidator", _SaudiArabic).Should().Be("أدخل قيمة.");
    }

    [Fact]
    public void should_throw_when_the_language_manager_is_null()
    {
        // when
        var egyptian = () => HeadlessLanguageManagerExtensions.AddHeadlessEgyptianArabicTranslations(null!);
        var saudi = () => HeadlessLanguageManagerExtensions.AddHeadlessSaudiArabicTranslations(null!);

        // then
        egyptian.Should().Throw<ArgumentNullException>();
        saudi.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_resolve_headless_validator_messages_in_each_register()
    {
        // given
        var previousCulture = FluentValidatorErrors.Culture;

        try
        {
            // when
            FluentValidatorErrors.Culture = _EgyptianArabic;
            var egyptian = FluentValidatorErrorDescriber.Strings.OnlyNumberValidator().Description;

            FluentValidatorErrors.Culture = _SaudiArabic;
            var saudi = FluentValidatorErrorDescriber.Strings.OnlyNumberValidator().Description;

            FluentValidatorErrors.Culture = _Arabic;
            var arabic = FluentValidatorErrorDescriber.Strings.OnlyNumberValidator().Description;

            // then
            egyptian.Should().Be("الحقل ده لازم يكون أرقام بس.");
            saudi.Should().Be("أدخل أرقاماً فقط.");
            arabic.Should().Be("يجب أن يحتوي الحقل على أرقام فقط.");
        }
        finally
        {
            FluentValidatorErrors.Culture = previousCulture;
        }
    }

    private static HashSet<string> _Placeholders(string template)
    {
        return _PlaceholderRegex.Matches(template).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
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

    [GeneratedRegex(@"\{[A-Za-z]+\}", RegexOptions.None, 100)]
    private static partial Regex _PlaceholderRegex { get; }

    private sealed record Person(string Name);

    private sealed class NameValidator : AbstractValidator<Person>
    {
        public NameValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
