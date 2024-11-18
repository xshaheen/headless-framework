// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Helpers.Normalizers;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.Normalizer;

[PublicAPI]
public sealed class FrameworkLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name.NormalizeName();

    public string? NormalizeEmail(string? email) => email.NormalizeEmail();
}
