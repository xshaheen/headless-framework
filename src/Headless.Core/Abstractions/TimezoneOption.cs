// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// An immutable time-zone display option: a display <see cref="Name"/> (identifier plus UTC offset)
/// and the <see cref="Value"/> identifier. Being immutable, instances are safe to cache and share.
/// </summary>
[PublicAPI]
public sealed record TimezoneOption(string Name, string Value);
