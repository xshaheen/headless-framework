using System.Text.Json.Serialization;

namespace Framework.Integrations.Recaptcha.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InternalRecaptchaV2Response))]
internal sealed partial class InternalRecaptchaV2ResponseContext : JsonSerializerContext;
