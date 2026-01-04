// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Queueing;

public interface IQueueBehavior<T>
    where T : class
{
    void Attach(IQueue<T> queue);
}
