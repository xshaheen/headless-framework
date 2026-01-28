// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Recaptcha.Contracts;

namespace Headless.Recaptcha.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV2Response))]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV3Response))]
internal sealed partial class ReCaptchaJsonSerializerContext : JsonSerializerContext;
