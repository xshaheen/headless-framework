using Headless.AuditLog;

namespace Tests.Fixture;

public sealed class Order : IAuditTracked
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";

    [AuditSensitive]
    public string Email { get; set; } = "";
    public bool IsDeleted { get; set; }
    public decimal Amount { get; set; }
}
