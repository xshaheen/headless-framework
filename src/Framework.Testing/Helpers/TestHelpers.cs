// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Framework.Testing.Helpers;

[PublicAPI]
public static class TestHelpers
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
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

    public static string PrettyPrintJson(this string json)
    {
        var obj = JsonSerializer.Deserialize<object>(json, JsonSerializerOptions);

        return JsonSerializer.Serialize(obj, JsonSerializerOptions);
    }

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
