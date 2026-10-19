// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

namespace Tests;

public sealed class OperationIdTransformerTests : TestBase
{
    [Theory]
    [InlineData(nameof(OperationIdActions.Upsert), "GET", "ReadUpsert")]
    [InlineData(nameof(OperationIdActions.Upsert), null, "ReadUpsert")]
    [InlineData(nameof(OperationIdActions.Upsert), "POST", "Actions_Upsert")]
    [InlineData(nameof(OperationIdActions.Blank), "GET", "Actions_Blank")]
    [InlineData(nameof(OperationIdActions.Whitespace), "GET", "Actions_Whitespace")]
    public async Task should_name_an_action_after_the_route_name_of_the_attribute_that_serves_the_method(
        string actionName,
        string? httpMethod,
        string expectedId
    )
    {
        var operation = new OpenApiOperation();

        await _TransformAsync(operation, _ActionDescription(actionName, actionName, httpMethod));

        operation.OperationId.Should().Be(expectedId);
    }

    [Fact]
    public async Task should_drop_the_async_suffix_from_an_action_name_that_keeps_it()
    {
        var operation = new OpenApiOperation();

        // MVC keeps the suffix when SuppressAsyncSuffixInActionNames is off.
        await _TransformAsync(operation, _ActionDescription(nameof(OperationIdActions.ListAsync), "ListAsync", "GET"));

        operation.OperationId.Should().Be("Actions_List");
    }

    [Fact]
    public async Task should_name_an_endpoint_after_its_route_name()
    {
        var operation = new OpenApiOperation();

        await _TransformAsync(
            operation,
            TransformerContexts.Description("GET", "orders", new RouteNameMetadata("ListOrders"))
        );

        operation.OperationId.Should().Be("ListOrders");
    }

    [Theory]
    [InlineData("POST", "orders/{id}/line-items", "PostOrdersByIdLineItems")]
    [InlineData("DELETE", "orders", "DeleteOrders")]
    [InlineData("GET", "things/{id:guid}", "GetThingsById")]
    [InlineData("GET", "things/{id?}", "GetThingsById")]
    [InlineData("GET", "things/{id:int=5}", "GetThingsById")]
    [InlineData("GET", "files/{**file-path}", "GetFilesByFilePath")]
    [InlineData("GET", "files/{*path}", "GetFilesByPath")]
    [InlineData("GET", "v{version:apiVersion}/things", "GetVThings")]
    [InlineData("GET", "reports/{year}-{month}", "GetReportsByYearByMonth")]
    [InlineData("GET", "orders/{id", "GetOrders")]
    [InlineData(null, null, "Get")]
    public async Task should_name_an_unnamed_endpoint_after_its_method_and_path(
        string? httpMethod,
        string? relativePath,
        string expectedId
    )
    {
        var operation = new OpenApiOperation();

        await _TransformAsync(operation, TransformerContexts.Description(httpMethod, relativePath));

        operation.OperationId.Should().Be(expectedId);
    }

    [Fact]
    public async Task should_replace_an_empty_operation_id_and_keep_a_set_one()
    {
        var empty = new OpenApiOperation { OperationId = "" };
        var named = new OpenApiOperation { OperationId = "Mine" };

        await _TransformAsync(empty, TransformerContexts.Description("GET", "orders"));
        await _TransformAsync(named, TransformerContexts.Description("GET", "orders"));

        empty.OperationId.Should().Be("GetOrders");
        named.OperationId.Should().Be("Mine");
    }

    private static ApiDescription _ActionDescription(string methodName, string actionName, string? httpMethod)
    {
        return new ApiDescription
        {
            HttpMethod = httpMethod,
            RelativePath = "actions",
            ActionDescriptor = new ControllerActionDescriptor
            {
                ControllerName = "Actions",
                ActionName = actionName,
                ControllerTypeInfo = typeof(OperationIdActions).GetTypeInfo(),
                MethodInfo = typeof(OperationIdActions).GetMethod(methodName)!,
            },
        };
    }

    private static Task _TransformAsync(OpenApiOperation operation, ApiDescription description) =>
        new OperationIdTransformer().TransformAsync(operation, TransformerContexts.Operation(description), AbortToken);
}

/// <summary>Action methods read for their HTTP method attributes; not a controller, so MVC never maps it.</summary>
public sealed class OperationIdActions
{
    [HttpGet(Name = "ReadUpsert")]
    [HttpPost]
    public void Upsert() { }

    [HttpGet(Name = "")]
    public void Blank() { }

    [HttpGet(Name = " ")]
    public void Whitespace() { }

    [HttpGet]
    public Task ListAsync() => Task.CompletedTask;
}
