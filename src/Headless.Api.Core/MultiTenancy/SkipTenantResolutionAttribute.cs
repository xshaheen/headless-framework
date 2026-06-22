// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Instructs <c>TenantResolutionMiddleware</c> to skip claim-based tenant resolution entirely
/// for the decorated endpoint. The request proceeds without an ambient tenant regardless of
/// whether the user is authenticated.
/// </summary>
/// <remarks>
/// Use on authentication, health-check, and other pre-authorization endpoints that must not
/// trigger tenant resolution. Differs from <see cref="AllowMissingTenantAttribute"/>, which still
/// allows resolution to run but relaxes the requirement that a tenant was found.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class SkipTenantResolutionAttribute : Attribute;
