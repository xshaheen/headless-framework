using Headless.Messaging.Messages;

namespace Tests;

public class MessageExtensionTest
{
    [Fact]
    public void GetIdTest()
    {
        var msgId = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = msgId };
        var message = new Message(header, null);

        message.GetId().Should().NotBeNull();
        message.GetId().Should().Be(msgId);
    }

    [Fact]
    public void GetNameTest()
    {
        var msgName = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = msgName };
        var message = new Message(header, null);

        message.GetName().Should().NotBeNull();
        message.GetName().Should().Be(msgName);
    }

    [Fact]
    public void GetCallbackNameTest()
    {
        var callbackName = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.CallbackName] = callbackName };
        var message = new Message(header, null);

        message.GetCallbackName().Should().NotBeNull();
        message.GetCallbackName().Should().Be(callbackName);
    }

    [Fact]
    public void GetGroupTest()
    {
        var group = Guid.NewGuid().ToString();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.Group] = group };
        var message = new Message(header, null);

        message.GetGroup().Should().NotBeNull();
        message.GetGroup().Should().Be(group);
    }

    [Fact]
    public void GetCorrelationSequenceTest()
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
    public void HasExceptionTest()
    {
        const string exception = "exception message";
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.Exception] = exception };
        var message = new Message(header, null);

        message.HasException().Should().BeTrue();
    }
}
