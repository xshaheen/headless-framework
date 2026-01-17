using System.Reflection;
using Framework.Messages.Internal;

namespace Tests;

public class HelperTest
{
    [Fact]
    public void IsControllerTest()
    {
        //Arrange
        var typeInfo = typeof(HomeController).GetTypeInfo();

        //Act
        var result = Helper.IsController(typeInfo);

        //Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsControllerAbstractTest()
    {
        //Arrange
        var typeInfo = typeof(AbstractController).GetTypeInfo();

        //Act
        var result = Helper.IsController(typeInfo);

        //Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(Uri))]
    public void IsSimpleTypeTest(Type type)
    {
        //Act
        var result = Helper.IsComplexType(type);

        //Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(HomeController))]
    [InlineData(typeof(Exception))]
    public void IsComplexTypeTest(Type type)
    {
        //Act
        var result = Helper.IsComplexType(type);

        //Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    public void IsInnerIpTest(string ipAddress)
    {
        Helper.IsInnerIp(ipAddress).Should().BeTrue();
    }
}

public class HomeController;

public abstract class AbstractController;
