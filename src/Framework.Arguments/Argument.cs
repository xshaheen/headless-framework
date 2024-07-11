namespace Framework.Arguments;

[PublicAPI]
public static partial class Argument
{
    private static string _AssertString(object? obj)
    {
        return obj switch
        {
            string => $"\"{obj}\"",
            null => "null",
            _ => $"<{obj}>",
        };
    }
}
