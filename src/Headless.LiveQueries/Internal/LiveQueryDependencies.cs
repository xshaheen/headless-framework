// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.LiveQueries.Internal;

internal sealed class LiveQueryDependencies : ILiveQueryDependencies
{
    public void DependsOn(params IEnumerable<string> keys)
    {
        Argument.IsNotNull(keys);

        string[] declared = [.. keys];

        foreach (var key in declared)
        {
            Argument.IsNotNullOrEmpty(key, paramName: nameof(keys));
        }

        LiveQueryRecorder.Current?.Add(declared);
    }
}
