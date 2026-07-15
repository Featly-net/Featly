using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// Boot-time seed for the auth layer: ensures the four immutable system roles
/// exist (or refreshes their permission set on upgrade) and provisions the
/// bootstrap admin user if <see cref="FeatlyAuthorizationOptions.BootstrapAdminIdentifier"/>
/// is configured. Both steps are idempotent — running on every boot is safe.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrap user is the chicken-and-egg solution from ARCHITECTURE.md §10:
/// "I need an Admin to create the first Admin." When the identifier is set and
/// no <see cref="User"/> exists for it, the service creates the row. After the
/// first admin exists, normal rules apply.
/// </para>
/// <para>
/// Order matters: the storage layer must be reachable (migrations applied) by
/// the time this runs, but the auth pipeline doesn't need it. Registered with
/// the default hosted-service order so it lines up after
/// <c>SqliteAutoMigrationHostedService</c>.
/// </para>
/// </remarks>
internal sealed partial class AuthBootstrapHostedService(
    StorageFacade store,
    IOptions<FeatlyAuthorizationOptions> options,
    IOptions<FeatlyServerOptions> serverOptions,
    ILogger<AuthBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 1. Seed (or refresh) the four immutable system roles. UpsertSystemRoleAsync
        //    is idempotent — keys are stable, ids stay put across boots, and the
        //    permission list refreshes so a new release that adds a permission to a
        //    system role lands automatically on existing installs.
        var templates = SystemRoles.Templates();
        foreach (var template in templates)
        {
            await store.Roles.UpsertSystemRoleAsync(template, cancellationToken).ConfigureAwait(false);
        }
        LogSystemRolesSeeded(logger, templates.Count);

        // 2. Bootstrap admin user, if configured and not yet present.
        await ProvisionBootstrapAdminAsync(cancellationToken).ConfigureAwait(false);

        // 3. Advise retiring the shared static admin key once a real admin can
        //    take over (issue #209). Advisory only — never disabled automatically.
        if (await BootstrapKeyAdvisor.ShouldWarnAsync(store, serverOptions.Value.AdminApiKey, cancellationToken).ConfigureAwait(false))
        {
            LogStaticAdminKeyShouldBeRetired(logger);
        }
    }

    private async Task ProvisionBootstrapAdminAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value.BootstrapAdminIdentifier;
        if (string.IsNullOrWhiteSpace(bootstrap))
        {
            LogBootstrapAdminNotConfigured(logger);
            return;
        }

        var existing = await store.Users.GetByIdentifierAsync(bootstrap, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            LogBootstrapAdminAlreadyExists(logger, bootstrap);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Identifier = bootstrap,
            DisplayName = bootstrap,
            Email = bootstrap.Contains('@', StringComparison.Ordinal) ? bootstrap : null,
            Disabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "bootstrap",
            UpdatedBy = "bootstrap",
        };
        await store.Users.UpsertAsync(user, "bootstrap", cancellationToken).ConfigureAwait(false);
        LogBootstrapAdminCreated(logger, bootstrap);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "Featly system roles seeded ({Count}).")]
    private static partial void LogSystemRolesSeeded(ILogger logger, int count);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Featly:Authorization:BootstrapAdminIdentifier is not set — skipping bootstrap admin provisioning.")]
    private static partial void LogBootstrapAdminNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information,
        Message = "Bootstrap admin '{Identifier}' already exists; nothing to seed.")]
    private static partial void LogBootstrapAdminAlreadyExists(ILogger logger, string identifier);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information,
        Message = "Provisioned bootstrap admin '{Identifier}'. Promote a real user before tightening AutoProvisionMode to Closed.")]
    private static partial void LogBootstrapAdminCreated(ILogger logger, string identifier);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning,
        Message = "A static Featly:Server:AdminApiKey is configured while a real admin user already exists. The static key is shared, unattributable, and non-rotatable — remove it and rely on per-user admin API keys.")]
    private static partial void LogStaticAdminKeyShouldBeRetired(ILogger logger);
}
