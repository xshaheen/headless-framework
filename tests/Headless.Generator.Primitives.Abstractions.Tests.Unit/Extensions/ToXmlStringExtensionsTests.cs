using Headless.Generator.Primitives;

namespace Tests.Extensions;

public sealed class ToXmlStringExtensionsTests
{
    [Fact]
    public void should_convert_int_to_xml_string()
    {
        // given
        const int value = 12345;

        // when
        var result = value.ToXmlString();

        // then
        result.Should().Be("12345");
    }

    [Fact]
    public void should_convert_decimal_to_xml_string()
    {
        // given
        const decimal value = 123.45m;

        // when
        var result = value.ToXmlString();

        // then
        result.Should().Be("123.45");
    }

    [Fact]
    public void should_convert_guid_to_xml_string()
    {
        // given
        var guid = Guid.NewGuid();

        // when
        var result = guid.ToXmlString();

        // then
        result.Should().Be(guid.ToString());
    }

    [Fact]
    public void should_convert_datetime_to_xml_string_with_expected_format()
    {
        // given
        var dateTime = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);

        // when
        var result = dateTime.ToXmlString();

        // then
        result.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$");
        result.Should().StartWith("2024-01-15T10:30:45");
    }

    [Fact]
    public void should_convert_dateonly_to_xml_string()
    {
        // given
        var dateOnly = new DateOnly(2024, 1, 15);

        // when
        var result = dateOnly.ToXmlString();

        // then
        result.Should().Be("2024-01-15");
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void should_convert_bool_to_xml_string(bool value, string expected)
    {
        // when
        var result = value.ToXmlString();

        // then
        result.Should().Be(expected);
    }
}
