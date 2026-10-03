// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging;

internal interface ICompletablePublishContext
{
    void MarkCompleted();
}
