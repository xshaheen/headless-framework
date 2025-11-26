// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.OpenApi.Nswag;

public sealed class HeadlessNswagOptions
{
    public bool AddBearerSecurity { get; set; } = true;

    public bool AddApiKeySecurity { get; set; }

    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    public bool AddPrimitiveMappings { get; set; } = true;
}
