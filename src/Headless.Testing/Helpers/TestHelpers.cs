// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Headless.Testing.Helpers;

/// <summary>
/// Miscellaneous test utilities: a shared frozen <see cref="System.Text.Json.JsonSerializerOptions"/> instance,
/// JSON pretty-printing, and an xUnit-wired <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>.
/// </summary>
[PublicAPI]
public static class TestHelpers
{
    /// <summary>
    /// Frozen, shared <see cref="System.Text.Json.JsonSerializerOptions"/> for test serialization.
    /// Settings: relaxed encoding, strict number handling, enums as strings, write-indented,
    /// skip null properties, case-sensitive property names. Immutable — consumers cannot mutate it.
    /// </summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = _CreateJsonSerializerOptions();

    private static JsonSerializerOptions _CreateJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = false,
            IgnoreReadOnlyProperties = false,
            IncludeFields = false,
            IgnoreReadOnlyFields = false,
            WriteIndented = true,
            AllowTrailingCommas = false,
            ReferenceHandler = null,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        };

        // Freeze the shared instance so a consumer cannot mutate it and pollute other tests.
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    /// <summary>
    /// Deserializes <paramref name="json"/> and re-serializes it using <see cref="JsonSerializerOptions"/>,
    /// producing an indented, normalized JSON string for use in assertions and test output.
    /// </summary>
    /// <param name="json">The JSON string to format.</param>
    /// <returns>A pretty-printed JSON string.</returns>
    public static string PrettyPrintJson(this string json)
    {
        var obj = JsonSerializer.Deserialize<object>(json, JsonSerializerOptions);

        return JsonSerializer.Serialize(obj, JsonSerializerOptions);
    }

    /// <summary>
    /// Creates an <see cref="ILoggerFactory"/> backed by an xUnit <c>XUnitLoggerProvider</c>.
    /// Log output is written to <paramref name="output"/> without log-level or category prefixes.
    /// </summary>
    /// <param name="output">
    /// The xUnit test output helper. When <see langword="null"/>, log messages are silently discarded.
    /// </param>
    /// <returns>
    /// A tuple of the underlying provider and the factory. Dispose both when the test ends.
    /// </returns>
    public static (ILoggerProvider Provider, ILoggerFactory Factory) CreateXUnitLoggerFactory(
        ITestOutputHelper? output = null
    )
    {
        var loggerFactory = new LoggerFactory();

        var loggerProvider = new XUnitLoggerProvider(
            output,
            new XUnitLoggerOptions
            {
                IncludeLogLevel = false,
                IncludeScopes = true,
                IncludeCategory = false,
            }
        );

        loggerFactory.AddProvider(loggerProvider);

        return (loggerProvider, loggerFactory);
    }
}
