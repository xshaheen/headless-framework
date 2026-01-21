// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Processor;

public interface IProcessor
{
    Task ProcessAsync(ProcessingContext context);
}
