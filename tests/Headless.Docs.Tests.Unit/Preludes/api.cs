// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application types, handlers, and variables the API guide's examples assume.

global using static ApiAmbient;
// ASP.NET Core namespaces outside the web SDK's implicit usings; a consumer's IDE adds these.
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Mvc;

public sealed class Order
{
    public UserId? UserId { get; init; }

    public string? TenantId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateOrderRequest(decimal Total);

public sealed record UpdateOrder(decimal Total);

public interface IOrderRepository
{
    Task<Order> CreateAsync(Order order, CancellationToken cancellationToken);
}

public interface IOrderService
{
    Task<ApiResult<Order>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ApiResult<Order>> GetAsync(int id, CancellationToken cancellationToken);

    Task<Order> UpdateAsync(Guid id, UpdateOrder request, EntityTag ifMatch, CancellationToken cancellationToken);
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class ApiAmbient
{
    public static Delegate handler => () => Results.Ok();

    public static IBlobStorage storageInstance => null!;

    public static IBlobContainerManager containerManager => null!;

    public static IResult CreateDisbursement() => Results.Ok();

    public static IResult HandleWebhook() => Results.Ok();
}
