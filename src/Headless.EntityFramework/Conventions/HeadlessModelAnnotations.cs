// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

internal static class HeadlessModelAnnotations
{
    internal static class Tenancy
    {
        internal const string IsOwned = "Headless:Tenant:IsOwned";
        internal const string PropertyName = "Headless:Tenant:PropertyName";
        internal const string ScopedIndex = "Headless:Tenant:ScopedIndex";
    }

    internal static class AuditLog
    {
        internal const string EntityIsAudited = "Headless:AuditLog:EntityIsAudited";
        internal const string PropertyIsExcluded = "Headless:AuditLog:PropertyIsExcluded";
        internal const string PropertyIsSensitive = "Headless:AuditLog:PropertyIsSensitive";
        internal const string PropertySensitiveStrategy = "Headless:AuditLog:PropertySensitiveStrategy";
    }

    internal static class Identity
    {
        internal const string TenantOwned = "Headless:Identity:TenantOwned";
    }
}
