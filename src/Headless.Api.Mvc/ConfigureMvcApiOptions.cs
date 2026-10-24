// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

/// <summary>
/// Configures <see cref="MvcOptions"/> and <see cref="ApiBehaviorOptions"/> with Headless API defaults.
/// </summary>
/// <remarks>
/// Applied automatically by <see cref="SetupMvc.ConfigureHeadlessMvc(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Behavior applied to <see cref="MvcOptions"/>:
/// <list type="bullet">
///   <item>Disables <c>HttpNoContentOutputFormatter.TreatNullValueAsNoContent</c> so that <c>Ok(null)</c> serializes normally instead of emitting 204.</item>
///   <item>Enables 406 Not Acceptable when the client's <c>Accept</c> header cannot be satisfied.</item>
///   <item>Clears the default model-validator providers and substitutes <c>SystemTextJsonValidationMetadataProvider</c>.</item>
///   <item>Applies <c>ProblemDetailsOptions.CustomizeProblemDetails</c> once to Headless-normalized MVC problem-details object results.</item>
///   <item>Binds an enum from a route, query, header, or form value by member name in any casing, rejecting numbers and unknown names, as a JSON body does.</item>
///   <item>Answers 400 <c>g:invalid_parameter_value</c> when a value cannot be bound, instead of running the action with it left at its default.</item>
///   <item>Binds an <see cref="IUploadedFile"/> from the multipart form and marks it as form-file input, so API Explorer describes it as a file part rather than expanding the interface.</item>
/// </list>
/// Behavior applied to <see cref="ApiBehaviorOptions"/>:
/// <list type="bullet">
///   <item>Suppresses the automatic 400 model-state filter added by <c>[ApiController]</c>, deferring validation to application-level handlers.</item>
/// </list>
/// </remarks>
internal sealed class ConfigureMvcApiOptions : IConfigureOptions<MvcOptions>, IConfigureOptions<ApiBehaviorOptions>
{
    /// <summary>Applies Headless MVC defaults to <paramref name="options"/>.</summary>
    /// <param name="options">The <see cref="MvcOptions"/> instance to configure.</param>
    public void Configure(MvcOptions options)
    {
        // Disable treat Ok(null) as NoContent. https://github.com/aspnet/AspNetCore/issues/8847
        var noContentFormatter = options.OutputFormatters.OfType<HttpNoContentOutputFormatter>().FirstOrDefault();

        noContentFormatter?.TreatNullValueAsNoContent = false;

        // Returns a 406 Not Acceptable if the MIME type in the Accept HTTP header is not valid.
        options.ReturnHttpNotAcceptable = true;

        // Clear the default model validator providers and add the System.Text.Json validation metadata provider.
        options.ModelValidatorProviders.Clear();
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());

        if (
            !options
                .Filters.OfType<TypeFilterAttribute>()
                .Any(filter => filter.ImplementationType == typeof(HeadlessProblemDetailsResultFilter))
        )
        {
            options.Filters.Add<HeadlessProblemDetailsResultFilter>();
        }

        if (!options.ModelBinderProviders.OfType<UploadedFileModelBinderProvider>().Any())
        {
            options.ModelBinderProviders.Insert(0, new UploadedFileModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(
                new BindingSourceMetadataProvider(typeof(IUploadedFile), BindingSource.FormFile)
            );
        }

        if (!options.ModelBinderProviders.OfType<EnumNameModelBinderProvider>().Any())
        {
            options.ModelBinderProviders.Insert(0, new EnumNameModelBinderProvider());
        }

        if (
            !options
                .Filters.OfType<TypeFilterAttribute>()
                .Any(filter => filter.ImplementationType == typeof(ModelBindingFailureFilter))
        )
        {
            options.Filters.Add<ModelBindingFailureFilter>();
        }

        // Exception mapping is handled globally by HeadlessApiExceptionHandler (registered in
        // AddHeadlessProblemDetails). No MVC IExceptionFilter needed.
    }

    /// <summary>Applies Headless API behavior defaults to <paramref name="options"/>.</summary>
    /// <param name="options">The <see cref="ApiBehaviorOptions"/> instance to configure.</param>
    public void Configure(ApiBehaviorOptions options)
    {
        // Disable the behavior that [ApiController] attribute makes model
        // validation errors automatically trigger an HTTP 400 response.
        options.SuppressModelStateInvalidFilter = true;
    }
}
