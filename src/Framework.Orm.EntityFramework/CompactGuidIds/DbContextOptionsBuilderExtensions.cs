using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Framework.Orm.EntityFramework.CompactGuidIds;

public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Replace the default EF value generation for string primary keys from Guid.ToString() with hyphens to Guid.ToString("N")
    /// </summary>
    public static DbContextOptionsBuilder GenerateCompactGuidForKeys(this DbContextOptionsBuilder builder)
    {
        builder.ReplaceService<IValueGeneratorSelector, CustomRelationalValueGeneratorSelector>();

        return builder;
    }
}
