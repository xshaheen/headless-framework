// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Models;

/// <summary>
/// Base problem details schema for OpenAPI documentation.
/// </summary>
public class HeadlessProblemDetails
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Detail { get; init; }
    public required string Instance { get; init; }
    public required string TraceId { get; init; }
    public required string BuildNumber { get; init; }
    public required string CommitNumber { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
