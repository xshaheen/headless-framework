using ObjectExtensions = System.ObjectExtensions;

namespace Tests.Core;

public sealed class ObjectExtensionsTests
{
    [Fact]
    public void in_tests()
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

    [Fact]
    public void as_tests()
    {
        object o1 = new ObjectExtensionsTests();
        ObjectExtensions.As<ObjectExtensionsTests>(o1).Should().NotBe(null);

        object? o2 = null;
        ObjectExtensions.As<ObjectExtensionsTests>(o2).Should().Be(null);
    }
}
