using System.Xml;
using Headless.Generator.Primitives;

namespace Tests.Extensions;

public sealed class XmlReaderExtensionsTests
{
    [Fact]
    public void should_read_int_from_xml()
    {
        // given
        var xml = "<value>123</value>";
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.Read(); // move to element

        // when
        var result = reader.ReadElementContentAs<int>();

        // then
        result.Should().Be(123);
    }

    [Fact]
    public void should_read_decimal_from_xml()
    {
        // given
        var xml = "<value>123.45</value>";
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.Read(); // move to element

        // when
        var result = reader.ReadElementContentAs<decimal>();

        // then
        result.Should().Be(123.45m);
    }

    [Fact]
    public void should_read_guid_from_xml()
    {
        // given
        var expected = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var xml = $"<value>{expected}</value>";
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.Read(); // move to element

        // when
        var result = reader.ReadElementContentAs<Guid>();

        // then
        result.Should().Be(expected);
    }
}
