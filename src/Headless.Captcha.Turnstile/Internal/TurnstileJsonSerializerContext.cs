// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(TurnstileSiteVerifyResponse))]
internal sealed partial class TurnstileJsonSerializerContext : JsonSerializerContext;
