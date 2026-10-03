// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Exceptions;

internal sealed class ExceptionDetailClassForSerialization
{
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
}
