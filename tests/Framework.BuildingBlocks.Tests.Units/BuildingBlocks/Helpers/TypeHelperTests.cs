namespace Framework.BuildingBlocks.Tests.Units.BuildingBlocks.Helpers;

public sealed class TypeHelperTests
{
    [Fact]
    public void GetDefaultValue_test()
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
