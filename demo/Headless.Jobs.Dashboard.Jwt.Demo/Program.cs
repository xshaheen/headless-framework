using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Headless.Jobs.Dashboard.Jwt.Demo;
using Headless.Jobs.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // DEMO ONLY — require HTTPS in production
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
#pragma warning disable CA5404
            ValidateIssuer = false, // DEMO ONLY — validate issuer in production
            ValidateAudience = false, // DEMO ONLY — validate audience in production
#pragma warning restore CA5404
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Query.ContainsKey("access_token"))
                {
                    context.Token = context.Request.Query["access_token"];
                }

                return Task.CompletedTask;
            },
        };
    });

const string dashboardPolicy = "DashboardPolicy";

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(dashboardPolicy, policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddHttpClient();

// Jobs setup — no AddOperationalStore() means in-memory persistence by default
builder.Services.AddHeadlessJobs(options =>
{
    options.AddDashboard(d => d.WithHostAuthentication(dashboardPolicy));
});

builder.Services.AddHostedService<DemoJobSeeder>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapGet("/", () => Results.LocalRedirect("/index.html", true));

app.MapPost(
    "/security/createToken",
    [AllowAnonymous]
    (User user) =>
    {
        if (user is { UserName: "bob", Password: "bob" })
        {
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity([
                    new Claim("Id", Guid.NewGuid().ToString()),
                    new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Email, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                ]),
                Expires = DateTime.UtcNow.AddMinutes(60),
                Issuer = "Test",
                Audience = "Test",
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha512Signature
                ),
            };
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var stringToken = tokenHandler.WriteToken(token);
            return Results.Ok(stringToken);
        }
        return Results.Unauthorized();
    }
);

app.UseAuthentication();
app.UseAuthorization();

await app.RunAsync();
