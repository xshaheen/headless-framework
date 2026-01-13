// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Orm.EntityFramework.Configurations;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Locales))]
internal sealed partial class EfCoreJsonSerializerContext : JsonSerializerContext;
