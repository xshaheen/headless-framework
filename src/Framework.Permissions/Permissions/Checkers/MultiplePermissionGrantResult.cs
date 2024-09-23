using Framework.Kernel.Checks;

namespace Framework.Permissions.Permissions.Checkers;

public class MultiplePermissionGrantResult
{
    public bool AllGranted => Result.Values.All(x => x == PermissionGrantResult.Granted);

    public bool AllProhibited => Result.Values.All(x => x == PermissionGrantResult.Prohibited);

    public Dictionary<string, PermissionGrantResult> Result { get; }

    public MultiplePermissionGrantResult()
    {
        Result = new Dictionary<string, PermissionGrantResult>(StringComparer.Ordinal);
    }

    public MultiplePermissionGrantResult(
        string[] names,
        PermissionGrantResult grantResult = PermissionGrantResult.Undefined
    )
    {
        Argument.IsNotNull(names);
        Result = new Dictionary<string, PermissionGrantResult>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            Result.Add(name, grantResult);
        }
    }
}

public enum PermissionGrantResult
{
    Undefined,
    Granted,
    Prohibited,
}
