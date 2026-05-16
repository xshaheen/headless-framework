// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace HeadlessShop.Ordering.Application;

public sealed record OrderDto(Guid Id, Guid ProductId, int Quantity);
