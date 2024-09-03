#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public sealed class ErrorDescriptor
{
    public ErrorDescriptor(
        string code,
        [LocalizationRequired] string description,
        ValidationSeverity severity = ValidationSeverity.Information
    )
    {
        Code = code;
        Description = description;
        Severity = severity;
    }

    public ErrorDescriptor(
        string code,
        [LocalizationRequired] string description,
        Dictionary<string, object> paramsDictionary,
        ValidationSeverity severity = ValidationSeverity.Information
    )
    {
        Code = code;
        Description = description;
        Severity = severity;
        _params = paramsDictionary;
    }

    private Dictionary<string, object>? _params;

    /// <summary>A distinct code indicating the cause of the error.</summary>
    public string Code { get; private init; }

    /// <summary>A human-readable description of the error.</summary>
    public string Description { get; private init; }

    /// <summary>The severity of the error.</summary>
    public ValidationSeverity Severity { get; private init; }

    /// <summary>Object containing parameter values related to the error.</summary>
    public IReadOnlyDictionary<string, object> Params => _params ?? [];

    public ErrorDescriptor WithParam(string key, object value)
    {
        _params ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _params[key] = value;

        return this;
    }

    public ErrorDescriptor WithParams(IReadOnlyDictionary<string, object> values)
    {
        _params ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            _params[key] = value;
        }

        return this;
    }

    public void Deconstruct(out string code, out string description, out ValidationSeverity severity)
    {
        code = Code;
        description = Description;
        severity = Severity;
    }
}

public enum ValidationSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2
}
