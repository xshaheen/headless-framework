namespace Tests.Fixture;

public sealed class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";

    public string Email { get; set; } = "";
    public bool IsDeleted { get; set; }
    public decimal Amount { get; set; }
}
