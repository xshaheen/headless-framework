// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Options;

namespace Framework.Api.Options;

[PublicAPI]
public sealed class ConfigureMvcApiOptions : IConfigureOptions<MvcOptions>, IConfigureOptions<ApiBehaviorOptions>
{
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

        // Add the ApiExceptionFilter to the global filter collection.
        options.Filters.Add<MvcApiExceptionFilter>();
    }

    public void Configure(ApiBehaviorOptions options)
    {
        // Disable the behavior that [ApiController] attribute makes model
        // validation errors automatically trigger an HTTP 400 response.
        options.SuppressModelStateInvalidFilter = true;
    }
}
