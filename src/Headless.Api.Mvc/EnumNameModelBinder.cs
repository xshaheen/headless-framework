// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

/// <summary>
/// Binds an enum from a route, query, header, or form value as the JSON body reads the same text, so
/// <c>?status=active</c>, <c>?status=Active</c>, and <c>{"status": "active"}</c> mean the same thing.
/// </summary>
/// <remarks>
/// MVC's default converter also accepts the member's number (<c>?status=1</c>) and any number at all, and a value it
/// cannot parse leaves the parameter at its default while the action runs. Here a value the body would reject (a
/// number, an unknown name, a member name its <c>[JsonStringEnumMemberName]</c> alias replaces) is a model-state error,
/// which <see cref="ModelBindingFailureFilter"/> answers with 400.
/// </remarks>
internal sealed class EnumNameModelBinder(RequestEnumValue names, bool nullable) : IModelBinder
{
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

        if (names.TryParse(value, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            _Fail(bindingContext);
        }

        return Task.CompletedTask;
    }

    private static void _Fail(ModelBindingContext bindingContext) =>
        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            $"The value of '{bindingContext.ModelName}' is not one of the names of {bindingContext.ModelType.Name}."
        );
}

/// <summary>Selects <see cref="EnumNameModelBinder"/> for an enum or nullable enum bound from a non-body source.</summary>
internal sealed class EnumNameModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.UnderlyingOrModelType;
        var source = context.BindingInfo.BindingSource;

        // A body is read by the input formatter, which already applies the JSON enum rules. A header is read by
        // HeaderModelBinder, which supplies the header value provider and then asks for this binder again with the
        // header source cleared; claiming it here would read an empty value provider. An explicit [ModelBinder] wins.
        if (
            !type.IsEnum
            || source == BindingSource.Body
            || source == BindingSource.Header
            || context.BindingInfo.BinderType is not null
        )
        {
            return null;
        }

        var options = context.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        // Options that write the enum as a number give the body no names, so MVC's own parsing stays.
        return RequestEnumValue.For(type, options) is { } names
            ? new EnumNameModelBinder(names, context.Metadata.IsReferenceOrNullableType)
            : null;
    }
}
