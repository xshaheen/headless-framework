// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework.MultiTenancy;

public sealed class CrossTenantWriteException(string message) : Exception(message);
