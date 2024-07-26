namespace Framework.BuildingBlocks.Tests.Units.Extensions.System;

public static class StringExtensionsCompilationTests
{
    public static void NullIfEmpty__should_allows_null()
    {
        _ = ((string?)null).NullIfEmpty();
    }

    public static void NullIfEmpty__should_returns_nullable()
    {
        var value = "".NullIfEmpty();
        CompilerAssert.Nullable(ref value);
    }
}
