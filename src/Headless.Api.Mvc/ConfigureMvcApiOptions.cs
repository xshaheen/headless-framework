// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
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
///   <item>Refuses an enum sent as a number in a route, query, header, or form value, as the JSON body does; names bind as MVC binds them, in any casing.</item>
///   <item>Runs every registered FluentValidation <c>IValidator&lt;T&gt;</c> of each action argument and answers one 422 with all their failures, as <c>ValidateArguments()</c> does for Minimal APIs.</item>
/// </list>
/// Behavior applied to <see cref="ApiBehaviorOptions"/>:
/// <list type="bullet">
///   <item>Keeps the automatic 400 that <c>[ApiController]</c> answers when a value cannot be bound, and shapes it as 400 <c>g:invalid_parameter_value</c> naming the value, or the plain bad-request problem for a body.</item>
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

        if (!options.ModelBinderProviders.OfType<EnumNumberRefusingModelBinderProvider>().Any())
        {
            options.ModelBinderProviders.Insert(0, new EnumNumberRefusingModelBinderProvider());
        }

        // One shared instance: a type-registered filter is built again for every action invocation.
        if (!options.Filters.OfType<ArgumentValidationActionFilter>().Any())
        {
            options.Filters.Add(ArgumentValidationActionFilter.Instance);
        }

        // Exception mapping is handled globally by HeadlessApiExceptionHandler (registered in
        // AddHeadlessProblemDetails). No MVC IExceptionFilter needed.
    }

    /// <summary>Applies Headless API behavior defaults to <paramref name="options"/>.</summary>
    /// <param name="options">The <see cref="ApiBehaviorOptions"/> instance to configure.</param>
    public void Configure(ApiBehaviorOptions options)
    {
        // MVC's validator providers are cleared, so model state holds only binding failures, and the [ApiController]
        // filter answers them before the action runs; rule validation stays with FluentValidation.
        options.SuppressModelStateInvalidFilter = false;
        options.InvalidModelStateResponseFactory = ModelBindingFailureResponse.Create;
    }
}
