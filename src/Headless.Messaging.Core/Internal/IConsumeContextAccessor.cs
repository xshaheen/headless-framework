// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal interface IConsumeContextAccessor
{
    ConsumeContext? Current { get; set; }
}

internal sealed class AsyncLocalConsumeContextAccessor : IConsumeContextAccessor
{
    private readonly AsyncLocal<ConsumeContextHolder> _holder = new();

    public ConsumeContext? Current
    {
        get => _holder.Value?.Context;
        set
        {
            _holder.Value ??= new ConsumeContextHolder();
            _holder.Value.Context = value;
        }
    }

    private sealed class ConsumeContextHolder
    {
        public ConsumeContext? Context { get; set; }
    }
}
