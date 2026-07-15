using Headless.Messaging.AzureServiceBus.Helpers;

namespace Tests.Helpers;

public sealed class ServiceBusHelpersTests
{
    [Fact]
    public void should_throw_argument_exception_when_get_broker_address_both_inputs_are_null()
    {
        // given
        const string? connectionString = null;
        const string? @namespace = null;

        // when & then
        Action testCode = () => ServiceBusHelpers.GetBrokerAddress(connectionString, @namespace);

        testCode
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Either connection string or namespace are required.*");
    }

    [Fact]
    public void should_return_namespace_when_get_broker_address_connection_string_is_null()
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
    public void should_return_extracted_namespace_when_get_broker_address_namespace_is_null()
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
    public void should_throw_invalid_operation_exception_when_get_broker_address_namespace_extraction_fails()
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
    public void should_return_namespace_when_get_broker_address_both_namespace_and_connection_string_are_provided()
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
    public void should_return_extracted_namespace_when_get_broker_address_connection_string_is_valid_and_namespace_is_empty()
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
    public void should_return_namespace_when_get_broker_address_connection_string_is_empty()
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
