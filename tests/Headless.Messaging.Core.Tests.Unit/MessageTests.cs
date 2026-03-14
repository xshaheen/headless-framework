// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessageTests : TestBase
{
    [Fact]
    public void should_create_message_with_default_constructor()
    {
        // when
        var message = new Message();

        // then
        message.Headers.Should().NotBeNull();
        message.Headers.Should().BeEmpty();
        message.Value.Should().BeNull();
    }

    [Fact]
    public void should_create_message_with_headers_and_value()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Faker.Random.Guid().ToString(),
            [Headers.MessageName] = "test.message",
        };
        var value = new { Name = "Test", Amount = 99.99m };

        // when
        var message = new Message(headers, value);

        // then
        message.Headers.Should().BeSameAs(headers);
        message.Value.Should().Be(value);
    }

    [Fact]
    public void should_throw_when_headers_is_null()
    {
        // when
        var act = () => new Message(null!, new { Name = "Test" });

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("headers");
    }

    [Fact]
    public void should_allow_null_value()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Faker.Random.Guid().ToString(),
        };

        // when
        var message = new Message(headers, null);

        // then
        message.Value.Should().BeNull();
        message.Headers.Should().NotBeEmpty();
    }

    [Fact]
    public void should_get_id_from_headers()
    {
        // given
        var messageId = Faker.Random.Guid().ToString();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = messageId };
        var message = new Message(headers, null);

        // when
        var result = message.GetId();

        // then
        result.Should().Be(messageId);
    }

    [Fact]
    public void should_get_name_from_headers()
    {
        // given
        const string messageName = "orders.placed";
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = messageName };
        var message = new Message(headers, null);

        // when
        var result = message.GetName();

        // then
        result.Should().Be(messageName);
    }

    [Fact]
    public void should_get_group_from_headers()
    {
        // given
        const string group = "order-service";
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.Group] = group };
        var message = new Message(headers, null);

        // when
        var result = message.GetGroup();

        // then
        result.Should().Be(group);
    }

    [Fact]
    public void should_return_null_when_group_not_present()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);

        // when
        var result = message.GetGroup();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_get_callback_name_from_headers()
    {
        // given
        const string callbackName = "response-handler";
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.CallbackName] = callbackName };
        var message = new Message(headers, null);

        // when
        var result = message.GetCallbackName();

        // then
        result.Should().Be(callbackName);
    }

    [Fact]
    public void should_return_null_when_callback_name_not_present()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);

        // when
        var result = message.GetCallbackName();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_get_correlation_sequence_from_headers()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.CorrelationSequence] = "42" };
        var message = new Message(headers, null);

        // when
        var result = message.GetCorrelationSequence();

        // then
        result.Should().Be(42);
    }

    [Fact]
    public void should_return_zero_when_correlation_sequence_not_present()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);

        // when
        var result = message.GetCorrelationSequence();

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void should_get_execution_instance_id_from_headers()
    {
        // given
        var instanceId = Faker.Random.Guid().ToString();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.ExecutionInstanceId] = instanceId,
        };
        var message = new Message(headers, null);

        // when
        var result = message.GetExecutionInstanceId();

        // then
        result.Should().Be(instanceId);
    }

    [Fact]
    public void should_return_null_when_execution_instance_id_not_present()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);

        // when
        var result = message.GetExecutionInstanceId();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_true_when_exception_header_present()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.Exception] = "InvalidOperationException-->Something failed",
        };
        var message = new Message(headers, null);

        // when
        var result = message.HasException();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_when_exception_header_not_present()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);

        // when
        var result = message.HasException();

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_add_exception_to_headers()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);
        var exception = new InvalidOperationException("Something went wrong");

        // when
        message.AddOrUpdateException(exception);

        // then
        message.Headers.Should().ContainKey(Headers.Exception);
        message.Headers[Headers.Exception].Should().Be("InvalidOperationException-->Something went wrong");
    }

    [Fact]
    public void should_update_exception_in_headers()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.Exception] = "OldException-->Old message",
        };
        var message = new Message(headers, null);
        var exception = new ArgumentException("New error", "testParam");

        // when
        message.AddOrUpdateException(exception);

        // then
        message.Headers[Headers.Exception].Should().Be("ArgumentException-->New error (Parameter 'testParam')");
    }

    [Fact]
    public void should_remove_exception_from_headers()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.Exception] = "SomeException-->Error message",
        };
        var message = new Message(headers, null);

        // when
        message.RemoveException();

        // then
        message.Headers.Should().NotContainKey(Headers.Exception);
    }

    [Fact]
    public void should_not_throw_when_removing_nonexistent_exception()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var message = new Message(headers, null);

        // when
        var act = () => message.RemoveException();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_use_ordinal_comparison_for_default_constructor()
    {
        // given
        var message = new Message();

        // when
        message.Headers["Test"] = "value";

        // then
        message.Headers.ContainsKey("Test").Should().BeTrue();
        message.Headers.ContainsKey("test").Should().BeFalse();
    }
}
