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
        internal const string EntityIsAudited = HeadlessAuditAnnotations.EntityIsAudited;
        internal const string PropertyIsExcluded = HeadlessAuditAnnotations.PropertyIsExcluded;
        internal const string PropertyIsSensitive = HeadlessAuditAnnotations.PropertyIsSensitive;
        internal const string PropertySensitiveStrategy = HeadlessAuditAnnotations.PropertySensitiveStrategy;
    }

    internal static class Identity
    {
        internal const string TenantOwned = "Headless:Identity:TenantOwned";
    }
}
