// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.Normalizer;

[PublicAPI]
public sealed class HeadlessLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => LookupNormalizer.NormalizeUserName(name);

    public string? NormalizeEmail(string? email) => LookupNormalizer.NormalizeEmail(email);
}
