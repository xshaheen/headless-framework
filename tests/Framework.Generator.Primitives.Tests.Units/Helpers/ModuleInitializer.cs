// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Runtime.CompilerServices;

namespace Tests.Helpers;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
    }
}
