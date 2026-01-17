// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;
using Framework.Messages.Monitoring;

namespace Framework.Messages.Internal;

public interface IMessageSender
{
    Task<OperateResult> SendAsync(MediumMessage message);
}
