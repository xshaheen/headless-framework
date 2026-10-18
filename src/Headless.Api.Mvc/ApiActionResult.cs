// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

#pragma warning disable CA1000 // IEndpointMetadataProvider requires a static member on the returned generic type.
namespace Headless.Api;

/// <summary>
/// The MVC action result of a valued <see cref="ApiResult{T}"/>: 200 OK with the value on success, or the mapped
/// problem-details response on failure. Declaring it as an action's return type publishes that whole response contract
/// to the OpenAPI document.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
/// <remarks>
/// MVC converts it to <see cref="Result"/> before result filters run, so filters and the response see the same
/// <see cref="OkObjectResult"/>, <see cref="NotFoundObjectResult"/>, or other typed result the controller helpers create.
/// </remarks>
[PublicAPI]
public sealed class ApiActionResult<T> : IConvertToActionResult, IEndpointMetadataProvider
{
    internal ApiActionResult(ActionResult result)
    {
        Result = result;
    }

    /// <summary>The action result MVC executes.</summary>
    public ActionResult Result { get; }

    IActionResult IConvertToActionResult.Convert() => Result;

    /// <summary>Publishes the complete HTTP response contract for OpenAPI discovery.</summary>
    /// <param name="method">The action method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status200OK, typeof(T));
    }
}

/// <summary>
/// The MVC action result of a unit <see cref="ApiResult"/>: 204 No Content on success, or the mapped problem-details
/// response on failure. Declaring it as an action's return type publishes that whole response contract to the OpenAPI
/// document.
/// </summary>
/// <remarks>
/// MVC converts it to <see cref="Result"/> before result filters run, so filters and the response see the same
/// <see cref="NoContentResult"/>, <see cref="NotFoundObjectResult"/>, or other typed result the controller helpers create.
/// </remarks>
[PublicAPI]
public sealed class ApiActionResult : IConvertToActionResult, IEndpointMetadataProvider
{
    internal ApiActionResult(ActionResult result)
    {
        Result = result;
    }

    /// <summary>The action result MVC executes.</summary>
    public ActionResult Result { get; }

    IActionResult IConvertToActionResult.Convert() => Result;

    /// <summary>Publishes the complete HTTP response contract for OpenAPI discovery.</summary>
    /// <param name="method">The action method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status204NoContent, successType: null);
    }
}
