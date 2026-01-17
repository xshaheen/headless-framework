using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Demo;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // AddCapWithOpenIdAuthorization(services);
        // AddCapWithAnonymousAccess(services);
        // AddCapWithCustomAuthorization(services);
        AddCapWithOpenIdAndCustomAuthorization(services);

        services.AddCors(x =>
        {
            x.AddDefaultPolicy(p =>
            {
                p.WithOrigins("https://localhost:5001").AllowCredentials().AllowAnyHeader().AllowAnyMethod();
            });
        });

        services.AddControllers();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseCors();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCookiePolicy();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

    public IServiceCollection AddCapWithOpenIdAuthorization(IServiceCollection services)
    {
        const string dashboardAuthorizationPolicy = "DashboardAuthorizationPolicy";

        services
            .AddAuthorization(options =>
            {
                options.AddPolicy(
                    dashboardAuthorizationPolicy,
                    policy =>
                        policy
                            .AddAuthenticationSchemes(OpenIdConnectDefaults.AuthenticationScheme)
                            .RequireAuthenticatedUser()
                );
            })
            .AddAuthentication(opt => opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.RequireHttpsMetadata = false;
                options.Authority = "https://demo.duendesoftware.com/";
                options.ClientId = "interactive.confidential";
                options.ClientSecret = "secret";
                options.ResponseType = "code";
                options.UsePkce = true;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
            });

        services.AddCap(cap =>
        {
            cap.UseDashboard(d =>
            {
                d.AllowAnonymousExplicit = false;
                d.AuthorizationPolicy = dashboardAuthorizationPolicy;
            });
            cap.UseInMemoryStorage();
            cap.UseInMemoryMessageQueue();
        });

        return services;
    }

    public IServiceCollection AddCapWithCustomAuthorization(IServiceCollection services)
    {
        const string myDashboardAuthenticationPolicy = "MyDashboardAuthenticationPolicy";

        services
            .AddAuthorization(options =>
            {
                options.AddPolicy(
                    myDashboardAuthenticationPolicy,
                    policy =>
                        policy
                            .AddAuthenticationSchemes(MyDashboardAuthenticationSchemeDefaults.Scheme)
                            .RequireAuthenticatedUser()
                );
            })
            .AddAuthentication()
            .AddScheme<MyDashboardAuthenticationSchemeOptions, MyDashboardAuthenticationHandler>(
                MyDashboardAuthenticationSchemeDefaults.Scheme,
                null
            );

        services.AddCap(cap =>
        {
            cap.UseDashboard(d =>
            {
                d.AuthorizationPolicy = myDashboardAuthenticationPolicy;
            });
            cap.UseInMemoryStorage();
            cap.UseInMemoryMessageQueue();
        });

        return services;
    }

    public IServiceCollection AddCapWithOpenIdAndCustomAuthorization(IServiceCollection services)
    {
        const string dashboardAuthorizationPolicy = "DashboardAuthorizationPolicy";

        services
            .AddAuthorization(options =>
            {
                options.AddPolicy(
                    dashboardAuthorizationPolicy,
                    policy =>
                        policy
                            .AddAuthenticationSchemes(
                                OpenIdConnectDefaults.AuthenticationScheme,
                                MyDashboardAuthenticationSchemeDefaults.Scheme
                            )
                            .RequireAuthenticatedUser()
                );
            })
            .AddAuthentication(opt => opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<MyDashboardAuthenticationSchemeOptions, MyDashboardAuthenticationHandler>(
                MyDashboardAuthenticationSchemeDefaults.Scheme,
                null
            )
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.RequireHttpsMetadata = false;
                options.Authority = "https://demo.duendesoftware.com/";
                options.ClientId = "interactive.confidential";
                options.ClientSecret = "secret";
                options.ResponseType = "code";
                options.UsePkce = true;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
            });

        services.AddCap(cap =>
        {
            cap.UseDashboard(d =>
            {
                d.AllowAnonymousExplicit = false;
                d.AuthorizationPolicy = dashboardAuthorizationPolicy;
            });
            cap.UseInMemoryStorage();
            cap.UseInMemoryMessageQueue();
        });

        return services;
    }

    public IServiceCollection AddCapWithAnonymousAccess(IServiceCollection services)
    {
        services.AddCap(cap =>
        {
            cap.UseDashboard(d =>
            {
                d.AllowAnonymousExplicit = true;
            });
            cap.UseInMemoryStorage();
            cap.UseInMemoryMessageQueue();
        });

        return services;
    }
}
