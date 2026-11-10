// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Compliance.Classification;

namespace Headless.Logging;

/// <summary>
/// Marks a member as <see cref="HeadlessDataClassifications.Private"/>: personal data about a person (pseudonymized with an HMAC key, erased otherwise).
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
public sealed class PrivateDataAttribute() : DataClassificationAttribute(HeadlessDataClassifications.Private);
