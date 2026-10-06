// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Implemented by an authorization requirement whose failure is not a missing right of the caller but a condition the
/// caller can be told about, such as a disabled feature. The requirement describes that failure as an
/// <see cref="ApiResultError"/>, and an HTTP host may answer with it instead of a challenge or forbid.
/// </summary>
/// <remarks>
/// <para>
/// The contract has no ASP.NET Core dependency, so a gate defined in a host-agnostic package can declare its failure
/// shape without referencing the HTTP layer. <c>Headless.Api</c> maps a failed authorization to the described error
/// only when every unmet requirement implements this interface, describes the same kind of error, and no handler
/// called <c>Fail</c> explicitly; any other failure keeps its 401 or 403. The error kind selects the status the same
/// way <see cref="ApiResult"/> errors map to responses: <see cref="ConflictError"/> 409, <see cref="ForbiddenError"/>
/// 403, <see cref="UnauthorizedError"/> 401, <see cref="NotFoundError"/> 404, <see cref="ValidationError"/> 422, and
/// any other error 409.
/// </para>
/// <para>
/// <see cref="DescribeFailure"/> runs per failed request, so it can return localized messages.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDescribedRequirement
{
    /// <summary>Describes why this requirement is unmet, for the response sent to the caller.</summary>
    /// <returns>The error to report.</returns>
    ApiResultError DescribeFailure();
}
