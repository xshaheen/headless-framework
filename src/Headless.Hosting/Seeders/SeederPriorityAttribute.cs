// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Seeders;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class)]
public sealed class SeederPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
