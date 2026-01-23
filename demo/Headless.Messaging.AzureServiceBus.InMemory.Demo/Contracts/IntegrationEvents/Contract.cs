namespace Demo.Contracts.IntegrationEvents;

public sealed record EntityCreatedForIntegration(Guid Id);

public sealed record EntityDeletedForIntegration(Guid Id);
