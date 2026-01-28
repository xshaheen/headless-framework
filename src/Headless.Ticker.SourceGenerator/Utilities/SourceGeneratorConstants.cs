using System.Text.RegularExpressions;

namespace Headless.Ticker.SourceGenerator.Utilities;

/// <summary>
/// Constants used throughout the TickerQ source generator for consistent behavior and performance.
/// </summary>
internal static class SourceGeneratorConstants
{
    #region Type Names

    public const string TickerFunctionAttributeName = "TickerFunctionAttribute";
    public const string CancellationTokenTypeName = "System.Threading.CancellationToken";
    public const string BaseTickerFunctionContextTypeName = "Headless.Ticker.Base.TickerFunctionContext";
    public const string BaseGenericTickerFunctionContextTypeName = "Headless.Ticker.Base.TickerFunctionContext`1";
    public const string FromKeyedServicesAttributeName = "FromKeyedServicesAttribute";

    #endregion

    #region File and Configuration

    public const string GeneratedFileName = "TickerQInstanceFactory.g.cs";
    public const string ConfigExpressionPrefix = "%";
    public const string ConfigExpressionSuffix = "%";
    public const int MinConfigExpressionLength = 2;

    #endregion

    #region Performance Constants
    public const int InitialStringBuilderCapacity = 8192; // Pre-allocate reasonable capacity

    public static readonly string[] CommonNamespaces =
    {
        "System",
        "System.Collections.Generic",
        "System.Threading",
        "System.Threading.Tasks",
        "Microsoft.Extensions.DependencyInjection",
    };

    // Pre-computed common variable names for performance (static readonly for better memory usage)
    public static readonly HashSet<string> CommonVariableNames = new(StringComparer.Ordinal)
    {
        "context",
        "service",
        "serviceProvider",
        "tickerFunctionDelegateDict",
        "cancellationToken",
        "genericContext",
        "requestTypes",
        "args",
        "sb",
        "delegates",
        "ctorCalls",
        "namespaces",
    };

    #endregion

    #region Regex Patterns


    // Pre-compiled regex patterns for optimal performance
    public static readonly Regex[] CompiledPatterns =
    [
        // Generic type parameters: typeof(Namespace.Type)
        new(
            @"typeof\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromMilliseconds(100)
        ),
        // Type declarations: new Namespace.Type()
        new(
            @"new\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromMilliseconds(100)
        ),
        // Type casts: (Namespace.Type)
        new(
            @"\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromMilliseconds(100)
        ),
        // Generic type arguments: <Namespace.Type>
        new(
            @"<\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromMilliseconds(100)
        ),
        // Static method calls: Namespace.Type.Method
        new(
            @"([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\.[A-Za-z_][A-Za-z0-9_]*\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromMilliseconds(100)
        ),
    ];

    #endregion
}
