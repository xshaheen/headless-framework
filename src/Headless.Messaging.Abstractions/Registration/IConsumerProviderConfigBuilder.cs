// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using Headless.Checks;

namespace Headless.Messaging.Registration;

internal interface IConsumerProviderConfigBuilder
{
    void SetConsumerProviderConfig(object config);
}
