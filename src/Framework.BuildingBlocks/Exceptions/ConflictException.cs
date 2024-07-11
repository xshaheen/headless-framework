#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

public sealed class ConflictException : Exception
{
    public ConflictException(ErrorDescriptor error)
        : base("Conflict: " + _BuildErrorMessage(error))
    {
        Errors = new[] { error };
    }

    public ConflictException(IReadOnlyList<ErrorDescriptor> errors)
        : base(_BuildErrorMessage(errors))
    {
        Errors = errors;
    }

    public ConflictException([LocalizationRequired] string error)
        : base($"Conflict: {error}")
    {
        Errors = new[] { new ErrorDescriptor("error", error) };
    }

    public ConflictException([LocalizationRequired] string error, Exception innerException)
        : base($"Conflict: {error}", innerException)
    {
        Errors = new[] { new ErrorDescriptor("error", error) };
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
