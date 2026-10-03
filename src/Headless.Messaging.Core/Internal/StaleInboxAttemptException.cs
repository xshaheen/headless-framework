// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class StaleInboxAttemptException(Guid storageId)
    : InvalidOperationException($"Inbox attempt '{storageId}' lost its generation fence before completion.");
