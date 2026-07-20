// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Processor;

internal interface IProcessor
{
    Task ProcessAsync(ProcessingContext context);
}
