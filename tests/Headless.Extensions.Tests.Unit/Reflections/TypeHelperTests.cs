// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;

namespace Tests.Reflections;

public sealed class TypeHelperTests
{
    [Fact]
    public void should_work_as_expected_when_get_default_value()
    {
        typeof(bool).GetDefaultValue().Should().Be(false);
        typeof(byte).GetDefaultValue().Should().Be(0);
        typeof(int).GetDefaultValue().Should().Be(0);
        typeof(string).GetDefaultValue().Should().BeNull();
        typeof(MyEnum).GetDefaultValue().Should().Be(MyEnum.MyValue0);
    }

    [Theory]
    [InlineData(typeof(byte), true)]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(ulong), true)]
    [InlineData(typeof(float), true)]
    [InlineData(typeof(double), true)] // regression: double was previously omitted
    [InlineData(typeof(char), true)] // regression: char was previously omitted
    [InlineData(typeof(decimal), true)]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(DateTime), true)]
    [InlineData(typeof(DateTimeOffset), true)]
    [InlineData(typeof(TimeSpan), true)]
    [InlineData(typeof(Guid), true)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(object), false)]
    [InlineData(typeof(int?), false)]
    public void is_non_nullable_primitive_type_should_match_expected(Type type, bool expected)
    {
        TypeHelper.IsNonNullablePrimitiveType(type).Should().Be(expected);
    }

    private enum MyEnum
    {
        MyValue0,
    }
}
