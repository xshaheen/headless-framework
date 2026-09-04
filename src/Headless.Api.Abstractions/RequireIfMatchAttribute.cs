// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Marks an endpoint as requiring one strong, Base64-encoded <c>If-Match</c> entity tag.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireIfMatchAttribute : Attribute;
