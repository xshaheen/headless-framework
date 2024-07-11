#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Configuration;

[PublicAPI]
public static class ConfigurationExtensions
{
    public static TModel GetOptions<TModel>(this IConfiguration configuration, string section)
        where TModel : new()
    {
        var model = new TModel();
        configuration.GetSection(section).Bind(model);

        return model;
    }

    /// <summary>
    /// Shorthand for GetSection("ConnectionStrings")[name].
    /// </summary>
    /// <param name="configuration">The configuration to enumerate.</param>
    /// <param name="name">The connection string key.</param>
    /// <returns>The connection string.</returns>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name)
    {
        return configuration.GetSection("ConnectionStrings")[name]
            ?? throw new InvalidOperationException($"Connection string '{name}' not found.");
    }
}
