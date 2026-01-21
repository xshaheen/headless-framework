// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

public interface IMessageSender
{
    Task<OperateResult> SendAsync(MediumMessage message);
}
