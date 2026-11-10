// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Compliance.Classification;

namespace Headless.Logging;

/// <summary>
/// Marks a member as <see cref="HeadlessDataClassifications.Secret"/>: a credential or key that must never appear in a log (always erased).
/// </summary>
[PublicAPI]
[AttributeUsage(
    AttributeTargets.Property
        | AttributeTargets.Field
        | AttributeTargets.Parameter
        | AttributeTargets.Class
        | AttributeTargets.Struct,
    AllowMultiple = true
)]
public sealed class SecretDataAttribute() : DataClassificationAttribute(HeadlessDataClassifications.Secret);
