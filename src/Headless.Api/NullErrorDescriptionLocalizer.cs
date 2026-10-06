// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api;

/// <summary>Default <see cref="IErrorDescriptionLocalizer"/> that keeps every description as written.</summary>
internal sealed class NullErrorDescriptionLocalizer : IErrorDescriptionLocalizer
{
    public static NullErrorDescriptionLocalizer Instance { get; } = new();

    public string? Localize(ErrorDescriptor error) => null;
}
