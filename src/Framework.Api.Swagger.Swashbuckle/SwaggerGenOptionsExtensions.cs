using System.Reflection;
using Framework.Arguments;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle;

[PublicAPI]
public static class SwaggerGenOptionsExtensions
{
    /// <summary>
    /// Includes the XML comment file if it has the same name as the assembly, a .xml file extension and exists in
    /// the same directory as the assembly.
    /// </summary>
    /// <param name="options">The Swagger options.</param>
    /// <param name="assembly">The assembly.</param>
    /// <returns><see langword="true"/> if the comment file exists and was added, otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">options or assembly.</exception>
    public static SwaggerGenOptions IncludeXmlCommentsIfExists(this SwaggerGenOptions options, Assembly assembly)
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(assembly);

        if (!string.IsNullOrEmpty(assembly.Location))
        {
            var filePath = Path.ChangeExtension(assembly.Location, ".xml");
            IncludeXmlCommentsIfExists(options, filePath);
        }

        return options;
    }

    /// <summary>Includes the XML comment file if it exists at the specified file path.</summary>
    /// <param name="options">The Swagger options.</param>
    /// <param name="filePath">The XML comment file path.</param>
    /// <returns><see langword="true"/> if the comment file exists and was added, otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">options or filePath.</exception>
    public static bool IncludeXmlCommentsIfExists(this SwaggerGenOptions options, string filePath)
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(filePath);

        if (File.Exists(filePath))
        {
            options.IncludeXmlComments(filePath);

            return true;
        }

        return false;
    }
}
