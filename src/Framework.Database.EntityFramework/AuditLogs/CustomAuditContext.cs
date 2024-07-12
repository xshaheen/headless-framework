using Audit.Core;
using Audit.Core.Providers;
using Audit.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Framework.Database.EntityFramework.AuditLogs;

public sealed class CustomAuditContext(DbContext dbContext) : DefaultAuditContext(dbContext)
{
    public override AuditDataProvider AuditDataProvider { get; set; } = new NullDataProvider();

    public override void OnScopeCreated(IAuditScope auditScope)
    {
        var auditLog = AuditLogCreator.CreateAudit(auditScope).ToList();

        if (auditLog.Count > 0)
        {
            DbContext.AddRange(auditLog);
        }
    }
}
