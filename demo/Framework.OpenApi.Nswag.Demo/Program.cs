// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning;
using Framework.Constants;
using Framework.OpenApi.Nswag;
using Framework.OpenApi.Scalar;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();

builder
    .Services.AddApiVersioning(options =>
    {
        options.ReportApiVersions = true;
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = new HeaderApiVersionReader(HttpHeaderNames.ApiVersion);
    })
    .AddApiExplorer(options =>
    {
        // Version format: 'v'major[.minor][-status]
        options.GroupNameFormat = "'v'VVV";
        options.DefaultApiVersion = new ApiVersion(1, 0);
    })
    .AddMvc();

builder.Services.AddControllers();
builder.Services.AddFrameworkNswagOpenApi();

builder
    .Services.AddAuthentication()
    .AddJwtBearer(
        AuthenticationConstants.Schemas.Bearer,
        options =>
        {
            if (builder.Environment.IsDevelopmentOrTest())
            {
                options.RequireHttpsMetadata = false;
                options.IncludeErrorDetails = true;
                IdentityModelEventSource.ShowPII = true;
            }

            options.SaveToken = true;
            options.Audience = "audience";
            options.ClaimsIssuer = "issuer";
            options.TokenValidationParameters.ValidIssuer = "issuer";
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.RequireExpirationTime = true;
            options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            options.TokenValidationParameters.NameClaimType = FrameworkClaimTypes.UserName;
            options.TokenValidationParameters.RoleClaimType = FrameworkClaimTypes.Roles;
            options.TokenValidationParameters.AuthenticationType = AuthenticationConstants.IdentityAuthenticationType;
            options.TokenValidationParameters.TokenDecryptionKey = createKey("EncryptingKey");
            options.TokenValidationParameters.IssuerSigningKey = createKey("SigningKey");

            return;

            static SymmetricSecurityKey createKey(string key) => new(Encoding.UTF8.GetBytes(key));
        }
    );

builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        "PolicyName",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(FrameworkClaimTypes.Roles, "role");
        }
    );

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapFrameworkNswagOpenApi();
app.MapFrameworkScalarOpenApi();
app.MapControllers();

await app.RunAsync();
