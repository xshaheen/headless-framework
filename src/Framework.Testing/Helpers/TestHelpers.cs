// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framework.Testing.Helpers;

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
}
