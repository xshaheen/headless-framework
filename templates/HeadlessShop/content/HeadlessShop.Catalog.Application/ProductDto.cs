// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace HeadlessShop.Catalog.Application;

public sealed record ProductDto(Guid Id, string Sku, string Name, decimal Price);
