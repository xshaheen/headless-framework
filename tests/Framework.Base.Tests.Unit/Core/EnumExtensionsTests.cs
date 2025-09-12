using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Framework.Primitives;

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
        displayName.Should().Be("ValueWithoutAttributes");
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
        var value = TestEnum.ValueWithAttributes;

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
        description.Should().BeEmpty();
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
                && l.Locale.Value == 1
            );
        locales
            .Should()
            .ContainSingle(l =>
                l.Key == "ar"
                && l.Locale.DisplayName == "نقدا"
                && l.Locale.Description == "الدفع نقدا"
                && l.Locale.Value == 1
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
}
