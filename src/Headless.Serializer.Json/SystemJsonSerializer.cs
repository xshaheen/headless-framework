// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// <see cref="IJsonSerializer"/> implementation backed by <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
/// <remarks>
/// Serialization and deserialization options are provided by the injected <see cref="IJsonOptionsProvider"/>.
/// When no provider is supplied the <see cref="DefaultJsonOptionsProvider"/> is used, which delegates to
/// <see cref="JsonConstants.DefaultWebJsonOptions"/>.
/// <para>
/// This class is annotated with <see cref="RequiresUnreferencedCodeAttribute"/> and
/// <see cref="RequiresDynamicCodeAttribute"/> because the reflection-based <c>System.Text.Json</c> path is
/// used internally. AOT-compatible scenarios should instead register a source-generated
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> via a custom
/// <see cref="IJsonOptionsProvider"/>.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SystemJsonSerializer(IJsonOptionsProvider? optionsProvider = null) : IJsonSerializer
{
    private readonly IJsonOptionsProvider _optionsProvider = optionsProvider ?? new DefaultJsonOptionsProvider();

    public T? Deserialize<T>(Stream data)
    {
        return JsonSerializer.Deserialize<T>(data, _optionsProvider.GetDeserializeOptions());
    }

    public object? Deserialize(Stream data, Type objectType)
    {
        return JsonSerializer.Deserialize(data, objectType, _optionsProvider.GetDeserializeOptions());
    }

    public void Serialize<T>(T? value, Stream output)
    {
        JsonSerializer.Serialize(output, value, _optionsProvider.GetSerializeOptions());
    }

    public void Serialize(object? value, Stream output)
    {
        JsonSerializer.Serialize(
            output,
            value,
            value is null ? typeof(object) : value.GetType(),
            _optionsProvider.GetSerializeOptions()
        );
    }
}
