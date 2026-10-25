// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Api.Options;

/// <summary>
/// Refuses an enum sent as a number in a route, query, header, or form value, then binds it as MVC does: by member name
/// in any casing.
/// </summary>
/// <remarks>
/// MVC's own enum binder reads the member name in any casing and records an unknown name as a binding failure, but it
/// also binds a number, which the JSON body refuses. Here a value with a numeric part is a binding failure, which the
/// <c>[ApiController]</c> check answers with 400 through <see cref="ModelBindingFailureResponse"/>; every other value goes
/// to MVC's binder unchanged. A <c>[JsonStringEnumMemberName]</c> alias binds only from the body.
/// </remarks>
internal sealed class EnumNumberRefusingModelBinder(IModelBinder inner) : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

        if (result == ValueProviderResult.None || !_HasNumber(result))
        {
            return inner.BindModelAsync(bindingContext);
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            $"The value of '{bindingContext.ModelName}' is a number, not a name of {bindingContext.ModelType.Name}."
        );

        return Task.CompletedTask;
    }

    private static bool _HasNumber(ValueProviderResult result)
    {
        foreach (var value in result)
        {
            if (value is not null && RequestEnumText.IsNumeric(value))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Puts <see cref="EnumNumberRefusingModelBinder"/> in front of MVC's own binder for an enum or nullable enum bound from
/// a non-body source.
/// </summary>
internal sealed class EnumNumberRefusingModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var source = context.BindingInfo.BindingSource;

        // A body is read by the input formatter, which applies the JSON enum rules. A header is read by
        // HeaderModelBinder, which supplies the header value provider and then asks for this binder again with the
        // header source cleared; claiming it here would read an empty value provider. An explicit [ModelBinder] wins.
        if (
            !context.Metadata.UnderlyingOrModelType.IsEnum
            || source == BindingSource.Body
            || source == BindingSource.Header
            || context.BindingInfo.BinderType is not null
        )
        {
            return null;
        }

        // The binder SimpleTypeModelBinderProvider would have chosen for an enum; this provider runs ahead of it.
        var inner = new SimpleTypeModelBinder(
            context.Metadata.ModelType,
            context.Services.GetRequiredService<ILoggerFactory>()
        );

        return new EnumNumberRefusingModelBinder(inner);
    }
}
