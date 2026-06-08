// Copyright (c) Mahmoud Shaheen. All rights reserved.

using HeadlessShop.Ordering.Application;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HeadlessShop.Ordering.Api;

public static class OrderingEndpoints
{
    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orders").RequireAuthorization();

        group
            .MapPost(
                "/",
                async (PlaceOrderRequest request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var order = await sender.Send(
                        new PlaceOrderCommand(request.ProductId, request.Quantity),
                        cancellationToken
                    );
                    return Results.Created($"/orders/{order.Id}", order);
                }
            )
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("Ordering_PlaceOrder");

        group
            .MapGet(
                "/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var order = await sender.Send(new GetOrderQuery(id), cancellationToken);
                    return order is null ? Results.NotFound() : Results.Ok(order);
                }
            )
            .Produces<OrderDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("Ordering_GetOrder");

        return endpoints;
    }
}

public sealed record PlaceOrderRequest(Guid ProductId, int Quantity);
