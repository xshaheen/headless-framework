// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Connekio.Internal;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ConnekioSingleSmsRequest))]
[JsonSerializable(typeof(ConnekioBatchSmsRequest))]
internal sealed partial class ConnekioJsonSerializerContext : JsonSerializerContext;
