// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Processor;

public interface IProcessor
{
    Task ProcessAsync(ProcessingContext context);
}
