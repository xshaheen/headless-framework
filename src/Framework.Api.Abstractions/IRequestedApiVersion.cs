// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Abstractions;

public interface IRequestedApiVersion
{
    string? Current { get; }
}
