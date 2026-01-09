// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api;
using Framework.Constants;
using Framework.OpenApi.Nswag;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHeadlessNswagOpenApi();

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
            options.TokenValidationParameters.NameClaimType = UserClaimTypes.UserName;
            options.TokenValidationParameters.RoleClaimType = UserClaimTypes.Roles;
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
            policy.RequireClaim(UserClaimTypes.Roles, "role");
        }
    );

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapFrameworkNswagOpenApi();
app.MapFrameworkScalarOpenApi();
app.MapControllers();

await app.RunAsync();
