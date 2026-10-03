// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using Headless.Checks;

namespace Headless.Messaging.Registration;

internal readonly record struct ProviderHeaderContribution(string HeaderName, Func<object, string?> Selector);
