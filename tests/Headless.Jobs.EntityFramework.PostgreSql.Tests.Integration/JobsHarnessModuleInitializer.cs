// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Tests;

internal static class JobsHarnessModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize() =>
        RuntimeHelpers.RunModuleConstructor(typeof(JobsCoordinationFixtureExtensions).Module.ModuleHandle);
}
