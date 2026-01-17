using System.Collections.Concurrent;
using Framework.Messages.Internal;
using Framework.Messages.Messages;

namespace Tests.Helpers;

public class TestThreadSafeMessageSender : IMessageSender
{
    private readonly ConcurrentQueue<MediumMessage> _messagesInOrder = [];

    public Task<OperateResult> SendAsync(MediumMessage message)
    {
        lock (_messagesInOrder)
        {
            _messagesInOrder.Enqueue(message);
        }
        return Task.FromResult(OperateResult.Success);
    }

    public int Count => _messagesInOrder.Count;
    public List<MediumMessage> ReceivedMessages => _messagesInOrder.ToList();
}
