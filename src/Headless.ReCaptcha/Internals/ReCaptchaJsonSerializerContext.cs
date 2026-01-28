// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;

namespace Headless.ReCaptcha.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV2Response))]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV3Response))]
internal sealed partial class ReCaptchaJsonSerializerContext : JsonSerializerContext;
