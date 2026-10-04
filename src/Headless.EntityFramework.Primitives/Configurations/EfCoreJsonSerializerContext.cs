// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.EntityFramework;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Locales))]
internal sealed partial class EfCoreJsonSerializerContext : JsonSerializerContext;
