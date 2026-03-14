// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

[PublicAPI]
public sealed record MessageDescriptor(string Code, [LocalizationRequired] string Description)
{
    public static implicit operator MessageDescriptor(string description) => new(description, description);

    public static MessageDescriptor ToMessageDescriptor(string description) => description;
}
