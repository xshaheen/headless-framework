// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.Orm.EntityFramework.ChangeTrackers;

public sealed class HeadlessNavigationEntry(NavigationEntry entry)
{
    public bool IsModified { get; set; }

    public string Name { get; } = entry.Metadata.Name;

    public NavigationEntry Entry { get; } = entry;

    public List<object>? OriginalValueList { get; private set; } = CalculateValueList(entry.CurrentValue);

    public void UpdateOriginalValueList()
    {
        var currentValue = CalculateValueList(Entry.CurrentValue);

        if (currentValue is null)
        {
            return;
        }

        if (OriginalValueList is null)
        {
            OriginalValueList = currentValue;

            return;
        }

        if (currentValue.Count > OriginalValueList.Count)
        {
            OriginalValueList = currentValue;
        }
    }

    public static List<object>? CalculateValueList(object? currentValue)
    {
        return currentValue switch
        {
            null => null,
            IEnumerable enumerable => [.. enumerable.Cast<object>()],
            _ => [currentValue],
        };
    }
}
