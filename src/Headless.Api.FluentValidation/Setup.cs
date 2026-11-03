// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.FluentValidation;

/// <summary>Registers the <see cref="IFileFormatInspector"/> that the file-signature validator rules take.</summary>
[PublicAPI]
public static class SetupFileFormatInspector
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a singleton <see cref="IFileFormatInspector"/> over every format FileSignatures ships, so a
        /// validator that calls <c>HaveSignatures(inspector, ...)</c> can take the inspector from its constructor. The
        /// registration is <c>TryAdd</c>, so an inspector the application registered first, for example one limited to
        /// the formats it accepts, is kept.
        /// </summary>
        /// <returns>The same service collection for chaining.</returns>
        public IServiceCollection AddHeadlessFileFormatInspector()
        {
            // The factory defers the format scan, which reflects over the FileSignatures assembly, to first resolution.
            services.TryAddSingleton<IFileFormatInspector>(static _ => new FileFormatInspector(
                FileFormatLocator.GetFormats()
            ));

            return services;
        }
    }
}
