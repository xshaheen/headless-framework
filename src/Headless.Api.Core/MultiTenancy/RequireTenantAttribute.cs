// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class RequireTenantAttribute : Attribute;
