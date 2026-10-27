// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Headless.OpenApi;

/// <summary>Gives MVC complex-parameter properties the explicit prefix the model binder requires.</summary>
internal sealed class BinderNameDescriptionProvider(IModelMetadataProvider? metadataProvider = null)
    : IApiDescriptionProvider
{
    // MVC fills the descriptions at -1000 but omits the top-level binder name from property paths.
    public int Order => -900;

    public void OnProvidersExecuting(ApiDescriptionProviderContext context)
    {
        foreach (var description in context.Results)
        {
            if (description.ActionDescriptor is not ControllerActionDescriptor)
            {
                continue;
            }

            foreach (var parameter in description.ParameterDescriptions)
            {
                if (
                    parameter.ModelMetadata?.MetadataKind != ModelMetadataKind.Property
                    || parameter.ParameterDescriptor is not { } root
                    // Form files bind greedily too, but still use the containing model's property path.
                    || (parameter.Source is { IsGreedy: true } source && source != BindingSource.FormFile)
                    || metadataProvider?.GetMetadataForType(root.ParameterType).IsComplexType != true
                    || GetBinderModelName(root, metadataProvider) is not { Length: > 0 } prefix
                )
                {
                    continue;
                }

                parameter.Name = ModelNames.CreatePropertyModelName(prefix, parameter.Name);
            }
        }
    }

    public void OnProvidersExecuted(ApiDescriptionProviderContext context) { }

    internal static string? GetBinderModelName(ParameterDescriptor parameter, IModelMetadataProvider? metadataProvider)
    {
        return parameter.BindingInfo?.BinderModelName
            ?? metadataProvider?.GetMetadataForType(parameter.ParameterType).BinderModelName;
    }
}
