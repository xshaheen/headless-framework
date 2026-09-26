// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application request and variables the Mediator guide's examples assume.

global using static MediatorAmbient;
using Mediator;

public sealed record CreateOrder(string ProductId) : IRequest<CreateOrderResponse>;

public sealed record CreateOrderResponse(Guid OrderId);

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class MediatorAmbient
{
    public static IMediator mediator => null!;

    public static ICurrentTenant currentTenant => null!;

    public static string tenantId => "";

    public static string productId => "";
}
