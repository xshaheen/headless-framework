// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sms.Dev.Internals;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IDictionary<string, object>))]
internal sealed partial class DevSmsJsonSerializerContext : JsonSerializerContext;
