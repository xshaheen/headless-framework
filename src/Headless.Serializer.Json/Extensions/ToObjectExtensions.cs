// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Serializer;

[PublicAPI]
public static class ToObjectExtensions
{
    /// <summary>
    /// Converts <paramref name="obj"/> to <typeparamref name="T"/> using the most appropriate strategy for
    /// the runtime type of the value.
    /// </summary>
    /// <remarks>
    /// Conversion strategy by source type:
    /// <list type="bullet">
    ///   <item><description><see cref="Guid"/> and <see cref="string"/> — via <see cref="System.ComponentModel.TypeDescriptor"/>.</description></item>
    ///   <item><description>Enum types — via <see cref="Enum.Parse(Type, string)"/>.</description></item>
    ///   <item><description><see cref="IConvertible"/> — via <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/> with <see cref="CultureInfo.InvariantCulture"/>.</description></item>
    ///   <item><description><see cref="System.Text.Json.JsonElement"/>, <see cref="System.Text.Json.JsonDocument"/>, <see cref="System.Text.Json.Nodes.JsonNode"/> — via <c>System.Text.Json</c> deserialization using <paramref name="options"/> (or <see cref="JsonConstants.DefaultInternalJsonOptions"/> when <see langword="null"/>).</description></item>
    ///   <item><description>Anything else — direct cast to <typeparamref name="T"/>.</description></item>
    /// </list>
    /// </remarks>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="obj">The value to convert. Returns <see langword="default"/> when <see langword="null"/>.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/> used when <paramref name="obj"/> is a JSON node type.
    /// Defaults to <see cref="JsonConstants.DefaultInternalJsonOptions"/>.
    /// </param>
    /// <returns>The converted value, or <see langword="default"/> when <paramref name="obj"/> is <see langword="null"/>.</returns>
    [RequiresUnreferencedCode("This method uses TypeDescriptor and JSON deserialization which require reflection.")]
    [RequiresDynamicCode("This method uses JSON deserialization which might require runtime code generation.")]
    public static T? To<T>(this object? obj, JsonSerializerOptions? options = null)
    {
        if (obj is null)
        {
            return default;
        }

        var baseType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(baseType) ?? baseType;

        // ToString() only in the branches that consume it: for JSON node inputs it would materialize the
        // entire raw payload as a string that the deserialization branches below never read.
        if (underlyingType == typeof(Guid) || underlyingType == typeof(string))
        {
            var text = obj.ToString();
            Debug.Assert(text is not null);

            return (T?)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text);
        }

        if (underlyingType.IsEnum)
        {
            var text = obj.ToString();
            Debug.Assert(text is not null);

            return (T?)Enum.Parse(underlyingType, text);
        }

        return obj switch
        {
            IConvertible => (T)Convert.ChangeType(obj, underlyingType, CultureInfo.InvariantCulture),
            JsonElement element => element.Deserialize<T>(options ?? JsonConstants.DefaultInternalJsonOptions),
            JsonDocument document => document.Deserialize<T>(options ?? JsonConstants.DefaultInternalJsonOptions),
            JsonNode node => node.Deserialize<T>(options ?? JsonConstants.DefaultInternalJsonOptions),
            _ => (T)obj,
        };
    }
}
