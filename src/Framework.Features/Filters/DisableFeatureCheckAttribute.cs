// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Features.Filters;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFeatureCheckAttribute : Attribute;
