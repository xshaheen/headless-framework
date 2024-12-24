// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning;
using Framework.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Framework.OpenApi.Nswag.Demo.Controllers;

[ApiController]
[Route("console")]
[ApiVersion(ApiVersions.V1)]
public sealed class TestController : ControllerBase
{
    [HttpGet("/get-endpoint")]
    [Authorize("PolicyName")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Get()
    {
        return Ok();
    }
}
