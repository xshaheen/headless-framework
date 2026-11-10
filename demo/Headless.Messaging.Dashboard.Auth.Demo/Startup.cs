using Headless.Dashboard;
using Headless.Messaging;
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
        // AddMessagingWithCustomScheme(services);
        AddMessagingWithOpenIdAndCustomScheme(services);

        services.AddControllers();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCookiePolicy();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHeadlessDashboard();
        });
    }

    public IServiceCollection AddMessagingWithOpenIdAuthorization(IServiceCollection services)
    {
        const string dashboardAuthorizationPolicy = "DashboardAuthorizationPolicy";

        // The dashboard evaluates its policies against the claims it copied into its own session, so this one lists
        // no authentication scheme: the host's cookie, filled by the OpenID Connect sign-in, is the default scheme.
        services
            .AddAuthorization(options =>
                options.AddPolicy(dashboardAuthorizationPolicy, policy => policy.RequireAuthenticatedUser())
            )
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

        _AddMessaging(services);

        // Opening /headless/auth/sign-in without a host sign-in challenges OpenID Connect once, then returns to the
        // dashboard with its session cookie set.
        services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication(host =>
                {
                    host.ViewPolicy = dashboardAuthorizationPolicy;
                    host.ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddMessaging()
        );

        return services;
    }

    // Host mode over the application's own authentication handler, which reads an X-Base-Token header. The browser
    // cannot send that header on a page load, so the operator exchanges it once for a dashboard session:
    // POST /headless/api/auth/session with X-Base-Token and X-Requested-With, then open /headless/. The policy lists
    // the scheme, so a session passes it only when that scheme authenticated the user it was issued for.
    public IServiceCollection AddMessagingWithCustomScheme(IServiceCollection services)
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

        _AddMessaging(services);

        services.AddHeadlessDashboard(dashboard =>
            dashboard.WithHostAuthentication(host => host.ViewPolicy = myDashboardAuthenticationPolicy).AddMessaging()
        );

        return services;
    }

    // OpenID Connect sign-in for operators in a browser, and the custom header scheme for a session exchange, under one
    // policy that accepts a user either scheme authenticated.
    public IServiceCollection AddMessagingWithOpenIdAndCustomScheme(IServiceCollection services)
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
                configureOptions: null
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

        _AddMessaging(services);

        services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication(host =>
                {
                    host.ViewPolicy = dashboardAuthorizationPolicy;
                    host.ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddMessaging()
        );

        return services;
    }

    public IServiceCollection AddMessagingWithAnonymousAccess(IServiceCollection services)
    {
        _AddMessaging(services);

        services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddMessaging());

        return services;
    }

    private static void _AddMessaging(IServiceCollection services)
    {
        services.AddHeadlessMessaging(setup =>
        {
            setup.AddModule<Headless.Messaging.Dashboard.Auth.Demo.MessagingModule>();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.UseInMemoryStorage();
            setup.UseInMemory();
        });
    }
}
