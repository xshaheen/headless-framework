namespace HeadlessShop.Ordering.Application;

public sealed record OrderView(Guid Id, Guid ProductId, int Quantity);
