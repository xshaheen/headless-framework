// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;

namespace Framework.Recaptcha.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV2Response))]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV3Response))]
internal sealed partial class ReCaptchaJsonSerializerContext : JsonSerializerContext;
