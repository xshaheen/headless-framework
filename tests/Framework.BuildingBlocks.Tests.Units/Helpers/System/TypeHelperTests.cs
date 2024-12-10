// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Helpers.System;

public sealed class TypeHelperTests
{
    [Fact]
    public void get_default_value_should_work_as_expected()
    {
        typeof(bool).GetDefaultValue().Should().Be(false);
        typeof(byte).GetDefaultValue().Should().Be(0);
        typeof(int).GetDefaultValue().Should().Be(0);
        typeof(string).GetDefaultValue().Should().BeNull();
        typeof(MyEnum).GetDefaultValue().Should().Be(MyEnum.MyValue0);
    }

    private enum MyEnum
    {
        MyValue0,
    }
}
