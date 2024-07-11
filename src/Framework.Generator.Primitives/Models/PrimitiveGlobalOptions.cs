namespace Primitives.Generator.Models;

/// <summary>Configuration options for controlling code generation of Primitive types.</summary>
internal sealed record PrimitiveGlobalOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to generate JSON converters for Primitive types.
    /// The default value is true.
    /// </summary>
    /// <value>
    ///   <c>true</c> if JSON converters should be generated; otherwise, <c>false</c>.
    /// </value>
    public bool GenerateJsonConverters { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether type converters should be generated for Primitive types.
    /// The default value is true.
    /// </summary>
    /// <value>
    ///   <c>true</c> if type converters should be generated; otherwise, <c>false</c>.
    /// </value>
    public bool GenerateTypeConverters { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to generate Swagger converters for Primitive types.
    /// </summary>
    /// <value><see langword="true"/> if Swagger converters should be generated; otherwise, <see langword="false"/>.</value>
    public bool GenerateSwashbuckleSwaggerConverters { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to generate Swagger converters for Primitive types.
    /// </summary>
    /// <value><see langword="true"/> if Swagger converters should be generated; otherwise, <see langword="false"/>.</value>
    public bool GenerateNswagSwaggerConverters { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether XML serialization should be generated for Primitive types.
    /// The default value is false.
    /// </summary>
    /// <value>
    ///   <c>true</c> if XML serialization methods should be generated; otherwise, <c>false</c>.
    /// </value>
    public bool GenerateXmlConverters { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Entity Framework Core value converters should be generated for Primitive types.
    /// </summary>
    /// <value>
    ///   <c>true</c> if Entity Framework Core value converters should be generated; otherwise, <c>false</c>.
    /// </value>
    public bool GenerateEntityFrameworkCoreValueConverters { get; set; }
}
