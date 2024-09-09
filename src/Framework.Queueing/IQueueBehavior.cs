// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Queueing;

public interface IQueueBehavior<T>
    where T : class
{
    void Attach(IQueue<T> queue);
}
