// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning;
using Headless.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Headless.OpenApi.Nswag.Demo.Controllers;

[ApiController]
[Route("console")]
[ApiVersion(HeadlessApiVersions.V1)]
[Produces("application/json", "application/problem+json")]
public sealed class TestController : ControllerBase
{
    /// <summary>
    /// Example of a GET endpoint.
    /// </summary>
    /// <returns>Example of return description</returns>
    [HttpGet("/get-endpoint")]
    [Authorize("PolicyName")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/json", "application/problem+json")]
    public ActionResult Get()
    {
        return Ok();
    }

    /// <summary>
    /// Example of a GET endpoint.
    /// </summary>
    /// <returns>Example of return description</returns>
    [HttpGet("/get-result-endpoint")]
    [Authorize("PolicyName")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ResponseItem> GetResult()
    {
        return Ok(
            new ResponseItem
            {
                Id = 1,
                Name = "Name",
                Description = "Description",
            }
        );
    }
}

public class ResponseItem
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string? Description { get; init; }
}
