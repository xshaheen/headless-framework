// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Orm.EntityFramework.Configurations;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Locales))]
internal sealed partial class EfCoreJsonSerializerContext : JsonSerializerContext;
