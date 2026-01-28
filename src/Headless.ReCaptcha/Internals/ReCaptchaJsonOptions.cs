// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;

namespace Headless.ReCaptcha.Internals;

internal static class ReCaptchaJsonOptions
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = ReCaptchaJsonSerializerContext.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };
}
