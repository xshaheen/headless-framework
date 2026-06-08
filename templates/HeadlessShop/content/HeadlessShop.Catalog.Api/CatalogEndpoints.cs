// Copyright (c) Mahmoud Shaheen. All rights reserved.

using HeadlessShop.Catalog.Application;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HeadlessShop.Catalog.Api;

public static class CatalogEndpoints
{
    public const string CreateProductPermission = "catalog.products.create";

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/catalog/products").RequireAuthorization();

        group
            .MapPost(
                "/",
                async (CreateProductRequest request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var product = await sender.Send(
                        new CreateProductCommand(request.Sku, request.Name, request.Price),
                        cancellationToken
                    );

                    return Results.Created($"/catalog/products/{product.Id}", product);
                }
            )
            .RequireAuthorization(CreateProductPermission)
            .Produces<ProductDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("Catalog_CreateProduct");

        group
            .MapGet(
                "/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var product = await sender.Send(new GetProductQuery(id), cancellationToken);
                    return product is null ? Results.NotFound() : Results.Ok(product);
                }
            )
            .Produces<ProductDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("Catalog_GetProduct");

        return endpoints;
    }
}

public sealed record CreateProductRequest(string Sku, string Name, decimal Price);
