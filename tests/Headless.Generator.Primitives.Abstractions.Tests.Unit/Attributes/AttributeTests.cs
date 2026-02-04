// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;

namespace Tests.Attributes;

public sealed class AttributeTests
{
    [Fact]
    public void should_create_primitive_assembly_attribute()
    {
        // when
        var attribute = new PrimitiveAssemblyAttribute();

        // then
        attribute.Should().NotBeNull();
    }

    [Fact]
    public void should_create_serialization_format_attribute_with_format()
    {
        // given
        const string format = "JSON";

        // when
        var attribute = new SerializationFormatAttribute(format);

        // then
        attribute.Format.Should().Be(format);
    }

    [Fact]
    public void should_create_string_length_attribute_with_properties()
    {
        // given
        const int min = 5;
        const int max = 100;
        const bool validate = false;

        // when
        var attribute = new StringLengthAttribute(min, max, validate);

        // then
        attribute.MinimumLength.Should().Be(min);
        attribute.MaximumLength.Should().Be(max);
        attribute.Validate.Should().BeFalse();
    }

    [Fact]
    public void should_create_string_length_attribute_with_default_validate()
    {
        // given
        const int min = 1;
        const int max = 50;

        // when
        var attribute = new StringLengthAttribute(min, max);

        // then
        attribute.MinimumLength.Should().Be(min);
        attribute.MaximumLength.Should().Be(max);
        attribute.Validate.Should().BeTrue();
    }

    [Fact]
    public void should_create_supported_operations_attribute_with_defaults()
    {
        // when
        var attribute = new SupportedOperationsAttribute();

        // then
        attribute.Addition.Should().BeFalse();
        attribute.Subtraction.Should().BeFalse();
        attribute.Multiplication.Should().BeFalse();
        attribute.Division.Should().BeFalse();
        attribute.Modulus.Should().BeFalse();
    }

    [Fact]
    public void should_create_supported_operations_attribute_with_custom_values()
    {
        // when
        var attribute = new SupportedOperationsAttribute
        {
            Addition = true,
            Subtraction = true,
            Multiplication = false,
            Division = true,
            Modulus = false,
        };

        // then
        attribute.Addition.Should().BeTrue();
        attribute.Subtraction.Should().BeTrue();
        attribute.Multiplication.Should().BeFalse();
        attribute.Division.Should().BeTrue();
        attribute.Modulus.Should().BeFalse();
    }

    [Fact]
    public void should_create_underlying_type_attribute_with_type()
    {
        // given
        var type = typeof(int);

        // when
        var attribute = new UnderlyingPrimitiveTypeAttribute(type);

        // then
        attribute.UnderlyingPrimitiveType.Should().Be(type);
    }
}
