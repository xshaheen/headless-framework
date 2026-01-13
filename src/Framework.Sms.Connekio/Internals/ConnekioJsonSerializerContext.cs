// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms.Connekio.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ConnekioSingleSmsRequest))]
[JsonSerializable(typeof(ConnekioBatchSmsRequest))]
internal sealed partial class ConnekioJsonSerializerContext : JsonSerializerContext;
