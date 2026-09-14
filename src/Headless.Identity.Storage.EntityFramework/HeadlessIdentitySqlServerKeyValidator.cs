// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework;

internal static class HeadlessIdentitySqlServerKeyValidator
{
    private const string _ProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const int _MaxKeyBytes = 900;

    internal static void Validate(string providerName, IReadOnlyList<IMutableEntityType?> entities)
    {
        if (string.Equals(providerName, _ProviderName, StringComparison.Ordinal))
        {
            foreach (var entity in entities.OfType<IMutableEntityType>())
            {
                foreach (var key in entity.GetKeys())
                {
                    // Identity's existing varbinary(1024) passkey PK is retained, including its upstream SQL Server limit.
                    if (entity != entities[7] || !key.IsPrimaryKey())
                    {
                        _ValidateKeyBudget(entity, key.Properties);
                    }
                }

                foreach (var foreignKey in entity.GetForeignKeys())
                {
                    _ValidateKeyBudget(entity, foreignKey.Properties);
                }
            }
        }
    }

    private static void _ValidateKeyBudget(IMutableEntityType entity, IReadOnlyList<IMutableProperty> properties)
    {
        var bytes = properties.Sum(_GetSqlServerKeyBytes);
        if (bytes > _MaxKeyBytes)
        {
            throw new InvalidOperationException(
                $"Identity key on '{entity.Name}' ({string.Join(", ", properties.Select(x => x.Name))}) requires {bytes.ToString(CultureInfo.CurrentCulture)} bytes, exceeding SQL Server's 900-byte primary/alternate/foreign-key budget. Configure compatible explicit key lengths before tenant opt-in."
            );
        }
    }

    private static long _GetSqlServerKeyBytes(IMutableProperty property)
    {
        var type = property.GetProviderClrType() ?? property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
        if (type == typeof(string) || type == typeof(byte[]))
        {
            var length = property.GetMaxLength();
            var bytesPerCharacter = type == typeof(string) && property.IsUnicode() != false ? 2 : 1;
            if (property.GetColumnType() is { } columnType)
            {
                var parts = columnType.ToLowerInvariant().Split('(', 2);
                bytesPerCharacter = parts[0].Trim() switch
                {
                    "nvarchar" or "nchar" => 2,
                    "varchar" or "char" or "varbinary" or "binary" => 1,
                    _ => throw new InvalidOperationException(
                        $"Cannot verify SQL Server Identity key mapping '{property.DeclaringType.Name}.{property.Name}' with column type '{columnType}'."
                    ),
                };
                if (parts.Length == 2)
                {
                    length = int.TryParse(parts[1].TrimEnd(')'), CultureInfo.InvariantCulture, out var configured)
                        ? configured
                        : null;
                }
            }

            if (length is not > 0)
            {
                throw new InvalidOperationException(
                    $"SQL Server Identity key property '{property.DeclaringType.Name}.{property.Name}' requires a bounded length."
                );
            }

            return (long)length.Value * bytesPerCharacter;
        }

        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean or TypeCode.Byte => 1,
            TypeCode.Int16 => 2,
            TypeCode.Int32 or TypeCode.Single => 4,
            TypeCode.Int64 or TypeCode.Double or TypeCode.DateTime => 8,
            TypeCode.Decimal => 17,
            _ when type == typeof(Guid) => 16,
            _ when type == typeof(DateTimeOffset) => 10,
            _ => throw new InvalidOperationException(
                $"Cannot verify SQL Server Identity key type '{type.Name}' for '{property.DeclaringType.Name}.{property.Name}'."
            ),
        };
    }
}
