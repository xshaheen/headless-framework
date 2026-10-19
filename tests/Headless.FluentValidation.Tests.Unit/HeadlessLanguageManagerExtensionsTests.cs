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
    private static readonly CultureInfo _Arabic = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo _English = CultureInfo.GetCultureInfo("en");

    // Every key FluentValidation 12 keeps a default message under; StringEnumValidator reads EnumValidator's.
    public static TheoryData<string> BuiltInKeys =>
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

    [Theory]
    [MemberData(nameof(BuiltInKeys))]
    public void should_translate_every_built_in_message_for_ar_eg_and_keep_ar_formal(string key)
    {
        // given
        var stock = new LanguageManager();
        var manager = new LanguageManager();

        // when
        manager.AddHeadlessEgyptianArabicTranslations();

        // then
        var egyptian = manager.GetString(key, _EgyptianArabic);
        egyptian.Should().NotBeNullOrWhiteSpace();
        egyptian.Should().NotBe(stock.GetString(key, _EgyptianArabic), "ar-EG must not fall back to formal Arabic");
        manager.GetString(key, _Arabic).Should().Be(stock.GetString(key, _Arabic));
        manager.GetString(key, _English).Should().Be(stock.GetString(key, _English));

        // The English template defines which placeholders the validator fills; an unknown one would print raw.
        _Placeholders(egyptian).Should().BeSubsetOf(_Placeholders(stock.GetString(key, _English)));
        _Placeholders(egyptian).Should().NotContain("{PropertyName}");
    }

    [Fact]
    public void should_report_ar_eg_ar_and_en_messages_when_validating_under_each_culture()
    {
        // given: validators read only the process-wide manager. Adding the ar-EG entries to it is what the API defaults
        // do at startup, and it changes no other culture's lookup.
        var stock = new LanguageManager();
        var global = ValidatorOptions.Global.LanguageManager.Should().BeAssignableTo<LanguageManager>().Subject;
        global.AddHeadlessEgyptianArabicTranslations();
        var validator = new NameValidator();

        // when
        var egyptian = _InCulture(_EgyptianArabic, () => validator.Validate(new Person(Name: "")));
        var arabic = _InCulture(_Arabic, () => validator.Validate(new Person(Name: "")));
        var english = _InCulture(_English, () => validator.Validate(new Person(Name: "")));

        // then
        egyptian.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("الحقل ده مطلوب.");
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
    public void should_let_a_later_translation_replace_the_egyptian_default()
    {
        // given
        var manager = new LanguageManager();
        manager.AddHeadlessEgyptianArabicTranslations();

        // when
        manager.AddTranslation("ar-EG", "NotEmptyValidator", "اكتب قيمة.");

        // then
        manager.GetString("NotEmptyValidator", _EgyptianArabic).Should().Be("اكتب قيمة.");
    }

    [Fact]
    public void should_resolve_headless_validator_messages_in_egyptian_arabic_under_ar_eg()
    {
        // given
        var previousCulture = FluentValidatorErrors.Culture;

        try
        {
            // when
            FluentValidatorErrors.Culture = _EgyptianArabic;
            var egyptian = FluentValidatorErrorDescriber.Strings.OnlyNumberValidator().Description;

            FluentValidatorErrors.Culture = _Arabic;
            var arabic = FluentValidatorErrorDescriber.Strings.OnlyNumberValidator().Description;

            // then
            egyptian.Should().Be("الحقل ده لازم يكون أرقام بس.");
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
