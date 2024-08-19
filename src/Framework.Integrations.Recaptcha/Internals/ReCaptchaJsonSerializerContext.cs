using System.Text.Json.Serialization;
using Framework.Integrations.Recaptcha.Contracts;

namespace Framework.Integrations.Recaptcha.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReCaptchaSiteVerifyResponse))]
[JsonSerializable(typeof(ReCaptchaSiteVerifyV3Response))]
internal sealed partial class ReCaptchaJsonSerializerContext : JsonSerializerContext;
