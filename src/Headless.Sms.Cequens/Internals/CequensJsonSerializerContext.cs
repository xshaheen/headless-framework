// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Cequens.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SendSmsRequest))]
[JsonSerializable(typeof(SigningInRequest))]
[JsonSerializable(typeof(SigningInResponse))]
internal sealed partial class CequensJsonSerializerContext : JsonSerializerContext;
