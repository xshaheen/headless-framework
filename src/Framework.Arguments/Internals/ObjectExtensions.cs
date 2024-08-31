using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Framework.Arguments.Internals;

internal static class ObjectExtensions
{
    public static string ToAssertString(this object? obj)
    {
        return obj switch
        {
            string => $"\"{obj}\"",
            null => "null",
            _ => $"<{obj.ToInvariantString()}>",
        };
    }

    [return: NotNullIfNotNull(nameof(obj))]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? ToInvariantString(this object? obj)
    {
        return obj switch
        {
            null => null,
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
            IConvertible c => c.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(format: null, CultureInfo.InvariantCulture),
            _ => obj.ToString(),
        };
    }
}
