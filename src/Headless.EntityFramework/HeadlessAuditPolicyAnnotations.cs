// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

internal static class HeadlessAuditPolicyAnnotations
{
    internal const string EntityIsAudited = "Headless:AuditLog:EntityIsAudited";
    internal const string PropertyIsExcluded = "Headless:AuditLog:PropertyIsExcluded";
    internal const string PropertyIsSensitive = "Headless:AuditLog:PropertyIsSensitive";
    internal const string PropertySensitiveStrategy = "Headless:AuditLog:PropertySensitiveStrategy";
}
