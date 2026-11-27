// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework;

/// <summary>The audit actor ids a save can be told not to fill from <c>ICurrentUser</c>.</summary>
[Flags]
internal enum HeadlessActorStamps
{
    None = 0,
    CreatedById = 1,
    UpdatedById = 2,
    DeletedById = 4,
    RestoredById = 8,
}
