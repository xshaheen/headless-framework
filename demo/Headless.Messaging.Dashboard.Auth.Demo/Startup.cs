using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Demo;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // AddMessagingWithOpenIdAuthorization(services);
        // AddMessagingWithAnonymousAccess(services);
        // AddMessagingWithCustomAuthorization(services);
        AddMessagingWithOpenIdAndCustomAuthorization(services);

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

    public IServiceCollection AddMessagingWithOpenIdAuthorization(IServiceCollection services)
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

        services.AddHeadlessMessaging(setup =>
        {
            setup.SubscribeFromAssembly(typeof(Startup).Assembly);
            setup.UseInMemoryStorage();
            setup.UseInMemoryMessageQueue();

            setup.UseDashboard(d => d.WithHostAuthentication(dashboardAuthorizationPolicy));
        });

        return services;
    }

    public IServiceCollection AddMessagingWithCustomAuthorization(IServiceCollection services)
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
                configureOptions: null
            );

        services.AddHeadlessMessaging(setup =>
        {
            setup.SubscribeFromAssembly(typeof(Startup).Assembly);
            setup.UseInMemoryStorage();
            setup.UseInMemoryMessageQueue();

            setup.UseDashboard(d => d.WithHostAuthentication(myDashboardAuthenticationPolicy));
        });

        return services;
    }

    public IServiceCollection AddMessagingWithOpenIdAndCustomAuthorization(IServiceCollection services)
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

        services.AddHeadlessMessaging(setup =>
        {
            setup.SubscribeFromAssembly(typeof(Startup).Assembly);
            setup.UseDashboard(d => d.WithHostAuthentication(dashboardAuthorizationPolicy));
            setup.UseInMemoryStorage();
            setup.UseInMemoryMessageQueue();
        });

        return services;
    }

    public IServiceCollection AddMessagingWithAnonymousAccess(IServiceCollection services)
    {
        services.AddHeadlessMessaging(setup =>
        {
            setup.SubscribeFromAssembly(typeof(Startup).Assembly);
            setup.UseDashboard(d => d.WithNoAuth());
            setup.UseInMemoryStorage();
            setup.UseInMemoryMessageQueue();
        });

        return services;
    }
}
