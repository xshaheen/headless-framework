// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.System;

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
