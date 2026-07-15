using Headless.Messaging;
using Headless.Messaging.Messages;

namespace Tests;

public sealed class MessageExtensionTest
{
    [Fact]
    public void get_id_test()
    {
        var msgId = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        message.Id.Should().NotBeNull();
        message.Id.Should().Be(msgId);
    }

    [Fact]
    public void get_name_test()
    {
        var msgName = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = msgName };
        var message = new Message(header, null);

        message.Name.Should().NotBeNull();
        message.Name.Should().Be(msgName);
    }

    [Fact]
    public void get_callback_name_test()
    {
        var callbackName = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.CallbackName] = callbackName };
        var message = new Message(header, null);

        message.GetCallbackName().Should().NotBeNull();
        message.GetCallbackName().Should().Be(callbackName);
    }

    [Fact]
    public void get_group_test()
    {
        var group = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.Group] = group };
        var message = new Message(header, null);

        message.GetGroup().Should().NotBeNull();
        message.GetGroup().Should().Be(group);
    }

    [Fact]
    public void get_correlation_sequence_test()
    {
        const int seq = 1;

        var header = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.CorrelationSequence] = seq.ToString(CultureInfo.InvariantCulture),
        };

        var message = new Message(header, null);

        message.GetCorrelationSequence().Should().Be(seq);
    }

    [Fact]
    public void has_exception_test()
    {
        const string exception = "exception message";
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.Exception] = exception };
        var message = new Message(header, null);

        message.HasException().Should().BeTrue();
    }
}
