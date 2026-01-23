namespace Demo.Contracts.DomainEvents;

public sealed record EntityCreated(Guid Id);

public sealed record EntityDeleted(Guid Id);
