// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal interface IConsumeContextAccessor
{
    ConsumeContext? Current { get; set; }
}
