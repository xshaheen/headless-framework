// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Demo;

public sealed record TodoView(Guid Id, string Title, bool Done, int OwnerId);
