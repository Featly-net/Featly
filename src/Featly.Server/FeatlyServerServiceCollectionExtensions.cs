using System.Text.Json.Serialization;
using Featly.Authorization;
using Featly.Server.Authentication;
using Featly.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Featly.Server;

/// <summary>DI extensions for registering Featly server-side services.</summary>
public static class FeatlyServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Featly server-side services. Storage must be added
    /// separately (for example, <c>services.AddFeatlyInMemoryStore()</c> or
    /// <c>services.AddFeatlySqliteStore(...)</c>).
    /// </summary>
    /// <param name="services">The DI container being configured.</param>
    /// <param name="configure">Optional in-line overrides for server options.</param>
    public static IServiceCollection AddFeatlyServer(
        this IServiceCollection services,
        Action<FeatlyServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind options from configuration (Featly:Server) and apply caller overrides.
        var optionsBuilder = services
            .AddOptions<FeatlyServerOptions>()
            .BindConfiguration(FeatlyServerOptions.SectionName);

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IPostConfigureOptions<FeatlyApiKeyAuthenticationOptions>,
            FeatlyApiKeyOptionsBridge>();

        services
            .AddAuthentication()
            .AddScheme<FeatlyApiKeyAuthenticationOptions, FeatlyApiKeyAuthenticationHandler>(
                FeatlyAuthenticationDefaults.AdminScheme,
                opts => opts.Scope = "AdminWrite")
            .AddScheme<FeatlyApiKeyAuthenticationOptions, FeatlyApiKeyAuthenticationHandler>(
                FeatlyAuthenticationDefaults.SdkScheme,
                opts => opts.Scope = "SdkRead")
            // Dashboard cookie session. HttpOnly + SameSite=Strict so an XSS in
            // the host can't read the cookie and a cross-site request can't
            // ride along. SecurePolicy=SameAsRequest keeps the local-dev story
            // simple (no HTTPS yet) while staying secure under TLS in prod.
            // Returning 401/403 instead of redirecting to a login URL keeps the
            // API JSON-shaped — the dashboard JS shows its own login screen.
            .AddCookie(FeatlyAuthenticationDefaults.CookieScheme, opts =>
            {
                opts.Cookie.Name = FeatlyAuthenticationDefaults.CookieName;
                opts.Cookie.HttpOnly = true;
                opts.Cookie.SameSite = SameSiteMode.Strict;
                opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                opts.Cookie.IsEssential = true;
                opts.ExpireTimeSpan = TimeSpan.FromDays(7);
                opts.SlidingExpiration = true;
                opts.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                opts.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            // Admin endpoints accept either Bearer (admin API key, used by SDK
            // clients and curl scripts) or the dashboard cookie session.
            .AddPolicy(FeatlyAuthenticationDefaults.AdminPolicy, policy => policy
                .AddAuthenticationSchemes(
                    FeatlyAuthenticationDefaults.AdminScheme,
                    FeatlyAuthenticationDefaults.CookieScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(FeatlyAuthenticationDefaults.SdkPolicy, policy => policy
                .AddAuthenticationSchemes(FeatlyAuthenticationDefaults.SdkScheme)
                .RequireAuthenticatedUser());

        // Authorization options (Featly:Authorization). Drives bootstrap admin
        // provisioning and (in M7+) auto-provision mode.
        services
            .AddOptions<FeatlyAuthorizationOptions>()
            .BindConfiguration(FeatlyAuthorizationOptions.SectionName);

        services.TryAddSingleton<IFeatlyPermissionChecker, DefaultFeatlyPermissionChecker>();
        services.TryAddSingleton<ApiKeyHasher>();
        services.TryAddSingleton<Approval.ChangeApplicationService>();
        services.TryAddSingleton<Approval.ChangeGate>();

        services.AddHostedService<DefaultProjectBootstrapHostedService>();
        services.AddHostedService<AuthBootstrapHostedService>();

        // Configure minimal API JSON to accept string enum values
        // (e.g. "Boolean" instead of 0) — the dashboard, curl examples,
        // and any human writing JSON expect named enums.
        services.Configure<JsonOptions>(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
        });

        return services;
    }

    /// <summary>
    /// Bridges <see cref="FeatlyServerOptions"/> (where keys are configured) into
    /// the per-scheme <see cref="FeatlyApiKeyAuthenticationOptions"/> instances.
    /// </summary>
    private sealed class FeatlyApiKeyOptionsBridge(
        Microsoft.Extensions.Options.IOptionsMonitor<FeatlyServerOptions> server)
        : IPostConfigureOptions<FeatlyApiKeyAuthenticationOptions>
    {
        public void PostConfigure(string? name, FeatlyApiKeyAuthenticationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var serverOptions = server.CurrentValue;
            options.ApiKey = options.Scope switch
            {
                "AdminWrite" => serverOptions.AdminApiKey,
                "SdkRead" => serverOptions.SdkApiKey,
                _ => options.ApiKey,
            };
        }
    }
}
