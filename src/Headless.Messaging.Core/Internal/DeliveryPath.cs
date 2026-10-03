// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal enum DeliveryPath
{
    Direct = 0,
    DurableStandalone = 1,
    DurableCoordinated = 2,
}
