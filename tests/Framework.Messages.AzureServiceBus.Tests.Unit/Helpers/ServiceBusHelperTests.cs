using Framework.Messages.Helpers;

namespace Tests.Helpers;

public class ServiceBusHelpersTests
{
    [Fact]
    public void GetBrokerAddress_ShouldThrowArgumentException_WhenBothInputsAreNull()
    {
        // given
        const string? connectionString = null;
        const string? @namespace = null;

        // when & then
        Action testCode = () => ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        testCode
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Either connection string or namespace are required.");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnNamespace_WhenConnectionStringIsNull()
    {
        // given
        const string? connectionString = null;
        const string? @namespace = "sb://mynamespace.servicebus.windows.net/";

        // when
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // then
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnExtractedNamespace_WhenNamespaceIsNull()
    {
        // given
        const string? connectionString =
            "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
        const string? @namespace = null;

        // when
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // then
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void GetBrokerAddress_ShouldThrowInvalidOperationException_WhenNamespaceExtractionFails()
    {
        // given
        const string? connectionString = "InvalidConnectionString";
        const string? @namespace = null;

        // when & then
        var func = () => ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        func.Should()
            .ThrowExactly<InvalidOperationException>()
            .WithMessage("Unable to extract namespace from connection string.");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnNamespace_WhenBothNamespaceAndConnectionStringAreProvided()
    {
        // given
        const string? connectionString =
            "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
        const string? @namespace = "anothernamespace";

        // when
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // then
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("anothernamespace");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnExtractedNamespace_WhenConnectionStringIsValidAndNamespaceIsEmpty()
    {
        // given
        const string? connectionString =
            "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";
        const string? @namespace = "";

        // when
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // then
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }

    [Fact]
    public void GetBrokerAddress_ShouldReturnNamespace_WhenConnectionStringIsEmpty()
    {
        // given
        const string? connectionString = "";
        const string? @namespace = "sb://mynamespace.servicebus.windows.net/";

        // when
        var result = ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        // then
        result.Name.Should().Be("servicebus");
        result.Endpoint.Should().Be("sb://mynamespace.servicebus.windows.net/");
    }
}
