using Framework.Messages.Helpers;

namespace Tests.Helpers;

public class ServiceBusHelpersTests
{
    [Fact]
    public void GetBrokerAddress_ShouldThrowArgumentException_WhenBothInputsAreNull()
    {
        // Arrange
        const string? connectionString = null;
        const string? @namespace = null;

        // Act & Assert
        Action testCode = () => ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        testCode
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Either connection string or namespace are required.");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnNamespace_WhenConnectionStringIsNull()
    {
        // Arrange
        const string? connectionString = null;
        const string? @namespace = "sb://mynamespace.servicebus.windows.net/";

        // Act
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // Assert
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnExtractedNamespace_WhenNamespaceIsNull()
    {
        // Arrange
        const string? connectionString =
            "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
        const string? @namespace = null;

        // Act
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // Assert
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void GetBrokerAddress_ShouldThrowInvalidOperationException_WhenNamespaceExtractionFails()
    {
        // Arrange
        const string? connectionString = "InvalidConnectionString";
        const string? @namespace = null;

        // Act & Assert
        var func = () => ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        func.Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage("Unable to extract namespace from connection string.");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnNamespace_WhenBothNamespaceAndConnectionStringAreProvided()
    {
        // Arrange
        const string? connectionString =
            "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
        const string? @namespace = "anothernamespace";

        // Act
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // Assert
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("anothernamespace");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnExtractedNamespace_WhenConnectionStringIsValidAndNamespaceIsEmpty()
    {
        // Arrange
        const string? connectionString =
            "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
        const string? @namespace = "";

        // Act
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // Assert
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnNamespace_WhenConnectionStringIsEmpty()
    {
        // Arrange
        const string? connectionString = "";
        const string? @namespace = "sb://mynamespace.servicebus.windows.net/";

        // Act
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // Assert
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }
}
