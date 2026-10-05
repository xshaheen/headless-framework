// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>
/// Model annotation names for the audit capture policy. They live with the policy extensions that write them, so a
/// package that maps entities through these primitives (Jobs, for one) can set the policy without referencing the full
/// Headless EF stack that reads them.
/// </summary>
internal static class HeadlessAuditAnnotations
{
    internal const string EntityIsAudited = "Headless:AuditLog:EntityIsAudited";
    internal const string PropertyIsExcluded = "Headless:AuditLog:PropertyIsExcluded";
    internal const string PropertyIsSensitive = "Headless:AuditLog:PropertyIsSensitive";
    internal const string PropertySensitiveStrategy = "Headless:AuditLog:PropertySensitiveStrategy";
}
