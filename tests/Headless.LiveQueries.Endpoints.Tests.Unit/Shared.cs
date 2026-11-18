// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>A value a test changes while request handlers on other threads read it.</summary>
internal sealed class Shared<T>(T value)
{
    private readonly Lock _lock = new();

    public T Value
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }
        set
        {
            lock (_lock)
            {
                field = value;
            }
        }
    } = value;
}
