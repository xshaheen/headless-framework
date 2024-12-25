// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Text;
using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.Normalizer;

[PublicAPI]
public sealed class FrameworkLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => LookupNormalizer.NormalizeUserName(name);

    public string? NormalizeEmail(string? email) => LookupNormalizer.NormalizeEmail(email);
}
