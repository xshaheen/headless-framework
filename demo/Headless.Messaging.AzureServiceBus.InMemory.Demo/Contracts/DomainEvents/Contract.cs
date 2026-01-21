namespace Demo.Contracts.DomainEvents;

public record EntityCreated(Guid Id);

public record EntityDeleted(Guid Id);
