// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Exceptions;

[PublicAPI]
public sealed class ConflictException : Exception
{
    public ConflictException(ErrorDescriptor error)
        : base("Conflict: " + _BuildErrorMessage(error))
    {
        Errors = [error];
    }

    public ConflictException(IReadOnlyList<ErrorDescriptor> errors)
        : base(_BuildErrorMessage(errors))
    {
        Errors = errors;
    }

    public ConflictException([LocalizationRequired] string error)
        : base($"Conflict: {error}")
    {
        Errors = [new ErrorDescriptor("error", error)];
    }

    public ConflictException([LocalizationRequired] string error, Exception innerException)
        : base($"Conflict: {error}", innerException)
    {
        Errors = [new ErrorDescriptor("error", error)];
    }

    public IReadOnlyList<ErrorDescriptor> Errors { get; }

    private static string _BuildErrorMessage(IEnumerable<ErrorDescriptor> errors)
    {
        var arr = errors.Select(error => $"{Environment.NewLine} -- {_BuildErrorMessage(error)}");

        return "Conflict:" + string.Concat(arr);
    }

    private static string _BuildErrorMessage(ErrorDescriptor error)
    {
        return $"{error.Code}: {error.Description}";
    }
}
