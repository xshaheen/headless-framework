// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal interface IConsumeContextAccessor
{
    ConsumeContext? Current { get; set; }
}

internal sealed class AsyncLocalConsumeContextAccessor : IConsumeContextAccessor
{
    private readonly AsyncLocal<ConsumeContext?> _current = new();

    public ConsumeContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
