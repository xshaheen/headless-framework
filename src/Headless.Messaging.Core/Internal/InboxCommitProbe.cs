// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal enum InboxCommitProbe
{
    Indeterminate = 0,
    Committed = 1,
}
