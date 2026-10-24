// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Headless.Api.Options;

/// <summary>
/// Binds an enum from a route, query, header, or form value by its member name in any casing, the way the Headless JSON
/// options read one from a body, so <c>?status=active</c>, <c>?status=Active</c>, and <c>{"status": "active"}</c> mean
/// the same thing.
/// </summary>
/// <remarks>
/// MVC's default converter also accepts the member's number (<c>?status=1</c>) and any number at all, and a value it
/// cannot parse leaves the parameter at its default while the action runs. Here a number or an unknown name is a
/// model-state error, which <see cref="ModelBindingFailureFilter"/> answers with 400, as the JSON body already rejects
/// both. A name set by <see cref="JsonStringEnumMemberNameAttribute"/> is accepted too, matching the body.
/// </remarks>
internal sealed class EnumNameModelBinder(Type enumType, bool nullable) : IModelBinder
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, object>> _Names = new();

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var value = result.FirstValue?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            if (nullable)
            {
                bindingContext.Result = ModelBindingResult.Success(model: null);
            }
            else
            {
                _Fail(bindingContext);
            }

            return Task.CompletedTask;
        }

        if (_TryParse(value, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            _Fail(bindingContext);
        }

        return Task.CompletedTask;
    }

    private bool _TryParse(string value, out object? parsed)
    {
        var names = _Names.GetOrAdd(enumType, _ReadNames);
        var isFlags = enumType.IsDefined(typeof(FlagsAttribute), inherit: false);
        ulong bits = 0;
        parsed = null;

        foreach (var part in isFlags ? value.Split(',') : [value])
        {
            if (!names.TryGetValue(part.Trim(), out var member))
            {
                return false;
            }

            bits |= Convert.ToUInt64(member, CultureInfo.InvariantCulture);
        }

        parsed = Enum.ToObject(enumType, bits);

        return true;
    }

    private static void _Fail(ModelBindingContext bindingContext) =>
        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            $"The value of '{bindingContext.ModelName}' is not one of the names of {bindingContext.ModelType.Name}."
        );

    private static IReadOnlyDictionary<string, object> _ReadNames(Type type)
    {
        var names = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var member = field.GetValue(null)!;
            names.TryAdd(field.Name, member);

            if (field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>() is { } alias)
            {
                names.TryAdd(alias.Name, member);
            }
        }

        return names;
    }
}

/// <summary>Selects <see cref="EnumNameModelBinder"/> for an enum or nullable enum bound from a non-body source.</summary>
internal sealed class EnumNameModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.UnderlyingOrModelType;

        // A body is read by the input formatter, which already applies the JSON enum rules.
        if (!type.IsEnum || context.BindingInfo.BindingSource == BindingSource.Body)
        {
            return null;
        }

        return new EnumNameModelBinder(type, context.Metadata.IsReferenceOrNullableType);
    }
}
