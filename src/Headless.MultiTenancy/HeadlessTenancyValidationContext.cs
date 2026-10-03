// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Validation context passed to tenant posture validators.</summary>
/// <param name="Services">The application service provider.</param>
/// <param name="Manifest">The shared tenant posture manifest.</param>
[PublicAPI]
public sealed record HeadlessTenancyValidationContext(IServiceProvider Services, TenantPostureManifest Manifest);
