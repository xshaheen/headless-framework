// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Helpers.Normalizers;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.Normalizer;

public sealed class FrameworkLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name.NormalizeName();

    public string? NormalizeEmail(string? email) => email.NormalizeEmail();
}
