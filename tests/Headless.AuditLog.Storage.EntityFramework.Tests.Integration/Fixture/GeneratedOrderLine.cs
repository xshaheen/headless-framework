using Headless.AuditLog;

namespace Tests.Fixture;

public sealed class GeneratedOrderLine : IAuditTracked
{
    public int Id { get; set; }

    public int GeneratedOrderId { get; set; }

    public GeneratedOrder? Order { get; set; }

    public string Sku { get; set; } = "";
}
