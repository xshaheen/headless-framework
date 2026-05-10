using Headless.AuditLog;

namespace Tests.Fixture;

public sealed class GeneratedOrder : IAuditTracked
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = "";
}
