// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Defines the naming convention to use when generating message names from message types.
/// </summary>
[PublicAPI]
public enum MessageNamingConvention
{
    /// <summary>
    /// Use the exact type name (e.g., "OrderCreated").
    /// </summary>
    TypeName = 0,

    /// <summary>
    /// Convert to kebab-case (e.g., "order-created").
    /// </summary>
    KebabCase = 1,
}
