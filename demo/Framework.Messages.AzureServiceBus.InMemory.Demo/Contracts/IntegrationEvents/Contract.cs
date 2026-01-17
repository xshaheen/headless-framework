namespace Demo.Contracts.IntegrationEvents;

public record EntityCreatedForIntegration(Guid Id);

public record EntityDeletedForIntegration(Guid Id);
