// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions.Internal;

/// <summary>Column naming shared by the permissions entity configurations.</summary>
internal static class PermissionsColumnNaming
{
    extension(EntityTypeBuilder builder)
    {
        /// <summary>
        /// Names every mapped column after its property in <paramref name="style"/>, so the EF mapping produces the
        /// same columns as the raw provider for that database.
        /// </summary>
        /// <remarks>
        /// Call last: it walks the properties already on the entity, including the ones conventions such as
        /// <c>ConfigureHeadlessConvention</c> add and name, and overrides those names.
        /// </remarks>
        public void ApplyColumnNaming(StorageNamingStyle style)
        {
            foreach (var property in builder.Metadata.GetProperties().ToList())
            {
                builder.Property(property.Name).HasColumnName(HeadlessStorageNaming.Apply(style, property.Name));
            }
        }
    }
}
