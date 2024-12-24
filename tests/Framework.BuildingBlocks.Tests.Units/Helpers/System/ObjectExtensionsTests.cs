// Copyright (c) Mahmoud Shaheen. All rights reserved.

using ObjectExtensions = System.ObjectExtensions;

namespace Tests.Helpers.System;

public sealed class ObjectExtensionsTests
{
    [Fact]
    public void As_tests()
    {
        object o1 = new ObjectExtensionsTests();
        ObjectExtensions.As<ObjectExtensionsTests>(o1).Should().NotBe(null);

        object? o2 = null;
        ObjectExtensions.As<ObjectExtensionsTests>(o2).Should().Be(null);
    }

    [Fact]
    public void To_tests()
    {
        "42".To<int>().Should().Be(42);
        "42".To<int>().Should().Be(42);

        "28173829281734".To<long>().Should().Be(28173829281734);
        "28173829281734".To<long>().Should().Be(28173829281734);

        "2.0".To<double>().Should().Be(2.0);
        "0.2".To<double>().Should().Be(0.2);
        2.0.To<int>().Should().Be(2);

        "false".To<bool>().Should().Be(false);
        "True".To<bool>().Should().Be(true);
        "False".To<bool>().Should().Be(false);
        "TrUE".To<bool>().Should().Be(true);

        var toBool = static () => "test".To<bool>();
        toBool.Should().ThrowExactly<FormatException>();
        var toInt = static () => "test".To<int>();
        toInt.Should().ThrowExactly<FormatException>();

        "2260AFEC-BBFD-42D4-A91A-DCB11E09B17F".To<Guid>().Should().Be(new Guid("2260afec-bbfd-42d4-a91a-dcb11e09b17f"));
    }

    [Fact]
    public void In_Tests()
    {
        5.In(1, 3, 5, 7).Should().Be(true);
        6.In(1, 3, 5, 7).Should().Be(false);

        int? number = null;
        number.In(2, 3, 5).Should().Be(false);

        var str = "a";
        str.In("a", "b", "c").Should().Be(true);

        str = null;
        str.In("a", "b", "c").Should().Be(false);
    }
}
