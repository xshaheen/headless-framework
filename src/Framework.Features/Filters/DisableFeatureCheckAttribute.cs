// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.Filters;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFeatureCheckAttribute : Attribute;
