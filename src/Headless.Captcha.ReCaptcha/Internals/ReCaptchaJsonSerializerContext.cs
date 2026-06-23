// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV2Response))]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV3Response))]
internal sealed partial class ReCaptchaJsonSerializerContext : JsonSerializerContext;
