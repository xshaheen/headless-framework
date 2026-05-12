using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Headless.Primitives;

namespace Tests.Core;

public sealed class EnumExtensionsTests
{
    public enum PaymentType
    {
        Unknown = 0,

        [Locale("en", "Cash", "Payment made by cash")]
        [Locale("ar", "نقدا", "الدفع نقدا")]
        Cash = 1,

        [Locale("en", "Online", "Payment made by online methods")]
        [Locale("ar", "عبر الإنترنت", "الدفع عبر الإنترنت")]
        Online = 2,
    }

    private enum TestEnum
    {
        [Display(Name = "Display Name")]
        [Description("Description Text")]
        ValueWithAttributes = 1,

        ValueWithoutAttributes = 2,
    }

    public enum SignedIntBackedEnum
    {
        [Locale("en", "Minus One")]
        MinusOne = -1,

        [Locale("en", "Zero")]
        Zero = 0,

        [Locale("en", "Int Min")]
        IntMin = int.MinValue,
    }

    public enum LongBackedEnum : long
    {
        [Locale("en", "Far Past")]
        FarPast = long.MinValue,

        [Locale("en", "Zero")]
        Zero = 0L,

        [Locale("en", "Far Future")]
        FarFuture = long.MaxValue,
    }

    public enum ULongBackedEnum : ulong
    {
        [Locale("en", "Zero")]
        Zero = 0UL,

        [Locale("en", "Max")]
        Max = ulong.MaxValue,
    }

    [Fact]
    public void GetDisplayName_should_return_display_name_when_DisplayAttribute_exists()
    {
        // given
        const TestEnum value = TestEnum.ValueWithAttributes;

        // when
        var displayName = value.GetDisplayName();

        // then
        displayName.Should().Be("Display Name");
    }

    [Fact]
    public void GetDisplayName_should_return_enum_name_when_DisplayAttribute_not_exists()
    {
        // given
        const TestEnum value = TestEnum.ValueWithoutAttributes;

        // when
        var displayName = value.GetDisplayName();

        // then
        displayName.Should().Be("Value without attributes");
    }

    [Fact]
    public void GetDisplayName_should_return_empty_string_when_enum_is_null()
    {
        // given
        TestEnum? value = null;

        // when
        var displayName = value.GetDisplayName();

        // then
        displayName.Should().BeEmpty();
    }

    [Fact]
    public void GetDescription_should_return_description_when_DescriptionAttribute_exists()
    {
        // given
        const TestEnum value = TestEnum.ValueWithAttributes;

        // when
        var description = value.GetDescription();

        // then
        description.Should().Be("Description Text");
    }

    [Fact]
    public void GetDescription_should_return_null_when_DescriptionAttribute_not_exists()
    {
        // given
        const TestEnum value = TestEnum.ValueWithoutAttributes;

        // when
        var description = value.GetDescription();

        // then
        description.Should().BeNull();
    }

    [Fact]
    public void GetDescription_should_return_empty_string_when_enum_is_null()
    {
        // given
        TestEnum? value = null;

        // when
        var description = value.GetDescription();

        // then
        description.Should().BeNull();
    }

    [Fact]
    public void GetLocale_should_return_all_locale_values_for_enum_member()
    {
        // given
        const PaymentType value = PaymentType.Cash;

        // when
        var locales = value.GetAllLocales().Locales;

        // then
        locales.Should().HaveCount(2);
        locales
            .Should()
            .ContainSingle(l =>
                l.Key == "en"
                && l.Locale.DisplayName == "Cash"
                && l.Locale.Description == "Payment made by cash"
                && l.Locale.Value == PaymentType.Cash
            );
        locales
            .Should()
            .ContainSingle(l =>
                l.Key == "ar"
                && l.Locale.DisplayName == "نقدا"
                && l.Locale.Description == "الدفع نقدا"
                && l.Locale.Value == PaymentType.Cash
            );
    }

    [Fact]
    public void GetLocale_should_return_empty_when_no_locale_attributes()
    {
        // given
        const PaymentType value = PaymentType.Unknown;

        // when
        var locales = value.GetAllLocales().Locales;

        // then
        locales.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SignedIntBackedEnum.MinusOne, "Minus One")]
    [InlineData(SignedIntBackedEnum.Zero, "Zero")]
    [InlineData(SignedIntBackedEnum.IntMin, "Int Min")]
    public void GetAllLocales_should_distinguish_negative_values_for_signed_int_backed_enums(
        SignedIntBackedEnum value,
        string expectedName
    )
    {
        // when
        var locales = value.GetAllLocales().Locales;

        // then — verifies signed→ulong bit-pattern keying doesn't collide negative values
        // with their positive counterparts after zero-extension
        locales.Should().ContainSingle(l => l.Key == "en" && l.Locale.DisplayName == expectedName);
    }

    [Theory]
    [InlineData(LongBackedEnum.FarPast, "Far Past")]
    [InlineData(LongBackedEnum.Zero, "Zero")]
    [InlineData(LongBackedEnum.FarFuture, "Far Future")]
    public void GetAllLocales_should_not_overflow_for_long_backed_enums(LongBackedEnum value, string expectedName)
    {
        // when
        var locales = value.GetAllLocales().Locales;

        // then
        locales.Should().ContainSingle(l => l.Key == "en" && l.Locale.DisplayName == expectedName);
    }

    [Theory]
    [InlineData(ULongBackedEnum.Zero, "Zero")]
    [InlineData(ULongBackedEnum.Max, "Max")]
    public void GetAllLocales_should_not_overflow_for_ulong_backed_enums(ULongBackedEnum value, string expectedName)
    {
        // when
        var locales = value.GetAllLocales().Locales;

        // then
        locales.Should().ContainSingle(l => l.Key == "en" && l.Locale.DisplayName == expectedName);
    }

    [Fact]
    public void GetAllLocales_should_distinguish_min_and_max_values_for_long_backed_enums()
    {
        // given
        const LongBackedEnum minValue = LongBackedEnum.FarPast;
        const LongBackedEnum maxValue = LongBackedEnum.FarFuture;

        // when
        var minLocales = minValue.GetAllLocales().Locales;
        var maxLocales = maxValue.GetAllLocales().Locales;

        // then — proves the raw-value cache key disambiguates extreme values rather than
        // colliding through int truncation (which would map both to 0 or similar)
        minLocales.Should().ContainSingle(l => l.Locale.DisplayName == "Far Past");
        maxLocales.Should().ContainSingle(l => l.Locale.DisplayName == "Far Future");
    }
}
