using System.Security.Claims;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for exporting and importing an environment's
/// <em>definitions</em> — its flags, configs, and segments — as a portable JSON
/// bundle. Backs the <c>featly export</c> / <c>featly import</c> CLI commands;
/// useful for backup, seeding, and moving config between environments.
/// </summary>
/// <remarks>
/// Operational state (users, API keys, role assignments, webhooks, audit) is
/// intentionally out of scope — a bundle carries only the targeting definitions.
/// Import upserts by key into the target environment (matching keys are
/// overwritten in place); it bypasses the approval gate. Both routes carry
/// dedicated permissions (<see cref="Permission.BackupExport"/> /
/// <see cref="Permission.BackupImport"/>): a bundle spans flags, configs, and
/// segments, so piggybacking on <c>FlagRead</c>/<c>FlagCreate</c> let a
/// flag-only role move entity kinds it could not touch individually
/// (SECURITY_AUDIT.md follow-up). By default only the system Admin role holds
/// them; grant them to a custom role for backup tooling.
/// </remarks>
internal static class AdminExportEndpoints
{
    public static RouteGroupBuilder MapAdminExport(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);
        admin.MapGet("/export", ExportAsync).WithName("Featly.Admin.Export").RequirePermission(Permission.BackupExport);
        admin.MapPost("/import", ImportAsync).WithName("Featly.Admin.Import").RequirePermission(Permission.BackupImport);
        return group;
    }

    // GET /admin/export?env=
    private static async Task<IResult> ExportAsync(string? env, StorageFacade store, CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound("No matching environment found.");
        }

        var flags = await store.Flags.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var configs = await store.Configs.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var segments = await store.Segments.ListAsync(environment.Id, ct).ConfigureAwait(false);

        return Results.Ok(new FeatlyExportBundle(environment.Key, DateTimeOffset.UtcNow, flags, configs, segments));
    }

    // POST /admin/import?env=
    private static async Task<IResult> ImportAsync(
        FeatlyExportBundle body,
        string? env,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.BadRequest("No matching environment found; create a project + environment first.");
        }
        // Import writes definitions directly; a ReadOnly freeze must reject it too
        // (issue #203). It still bypasses the approval gate by design (a backup
        // restore is an admin-only, all-or-nothing operation).
        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        var actor = ResolveActor(user);
        var envId = environment.Id;
        int flags = 0, configs = 0, segments = 0;

        foreach (var flag in body.Flags ?? [])
        {
            var existing = await store.Flags.GetAsync(envId, flag.Key, ct).ConfigureAwait(false);
            await store.Flags.UpsertAsync(envId, RebindFlag(flag, envId, existing?.Id ?? Guid.NewGuid()), actor, ct).ConfigureAwait(false);
            flags++;
        }

        foreach (var config in body.Configs ?? [])
        {
            var existing = await store.Configs.GetAsync(envId, config.Key, ct).ConfigureAwait(false);
            await store.Configs.UpsertAsync(envId, RebindConfig(config, envId, existing?.Id ?? Guid.NewGuid()), actor, ct).ConfigureAwait(false);
            configs++;
        }

        foreach (var segment in body.Segments ?? [])
        {
            var existing = await store.Segments.GetAsync(envId, segment.Key, ct).ConfigureAwait(false);
            await store.Segments.UpsertAsync(envId, RebindSegment(segment, envId, existing?.Id ?? Guid.NewGuid()), actor, ct).ConfigureAwait(false);
            segments++;
        }

        // An import rewrites the snapshot wholesale, so it must announce like any
        // other write: bump the version (SDK caches revalidate) and push the SSE
        // notification. Previously it did neither and clients only noticed on
        // their next poll, because the ETag was derived from max(UpdatedAt).
        if (flags + configs + segments > 0)
        {
            await SnapshotChange.AnnounceAsync(store, envId, "Environment", environment.Key, ct).ConfigureAwait(false);
        }

        await events.PublishAsync(
            FeatlyEventTypes.ConfigurationImported,
            entityType: "Environment",
            entityKey: environment.Key,
            environmentId: envId,
            user: user,
            data: new { flags, configs, segments },
            ct).ConfigureAwait(false);

        return Results.Ok(new ImportResult(environment.Key, flags, configs, segments));
    }

    // Re-bind imported definitions onto the target environment with a stable id:
    // reuse the existing row's id when the key is already present (overwrite in
    // place), otherwise a fresh id so importing into a different environment on
    // the same database never collides with the source row's primary key.
    private static Flag RebindFlag(Flag f, Guid environmentId, Guid id) => new()
    {
        Id = id,
        Key = f.Key,
        Name = f.Name,
        Description = f.Description,
        Type = f.Type,
        Enabled = f.Enabled,
        DefaultVariantKey = f.DefaultVariantKey,
        Variants = f.Variants,
        Rules = f.Rules,
        EnvironmentId = environmentId,
        Tags = f.Tags,
        Archived = f.Archived,
        CreatedAt = f.CreatedAt == default ? DateTimeOffset.UtcNow : f.CreatedAt,
        CreatedBy = string.IsNullOrEmpty(f.CreatedBy) ? "import" : f.CreatedBy,
    };

    private static Config RebindConfig(Config c, Guid environmentId, Guid id) => new()
    {
        Id = id,
        Key = c.Key,
        Name = c.Name,
        Description = c.Description,
        Type = c.Type,
        DefaultValue = c.DefaultValue,
        Rules = c.Rules,
        EnvironmentId = environmentId,
        Tags = c.Tags,
        Archived = c.Archived,
        CreatedAt = c.CreatedAt == default ? DateTimeOffset.UtcNow : c.CreatedAt,
        CreatedBy = string.IsNullOrEmpty(c.CreatedBy) ? "import" : c.CreatedBy,
    };

    private static Segment RebindSegment(Segment s, Guid environmentId, Guid id) => new()
    {
        Id = id,
        Key = s.Key,
        Name = s.Name,
        Description = s.Description,
        Conditions = s.Conditions,
        EnvironmentId = environmentId,
        CreatedAt = s.CreatedAt == default ? DateTimeOffset.UtcNow : s.CreatedAt,
        CreatedBy = string.IsNullOrEmpty(s.CreatedBy) ? "import" : s.CreatedBy,
    };

    private static Task<Environment?> ResolveEnvironmentAsync(StorageFacade store, string? envKey, CancellationToken ct)
        => EnvironmentResolver.ResolveAsync(store, envKey, ct);

    private static string ResolveActor(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        return string.IsNullOrEmpty(name) ? "anonymous" : name;
    }
}

/// <summary>A portable bundle of an environment's flag / config / segment definitions.</summary>
public sealed record FeatlyExportBundle(
    string EnvironmentKey,
    DateTimeOffset ExportedAt,
    IReadOnlyList<Flag> Flags,
    IReadOnlyList<Config> Configs,
    IReadOnlyList<Segment> Segments);

/// <summary>Outcome of an import: how many definitions of each kind were upserted.</summary>
public sealed record ImportResult(string EnvironmentKey, int Flags, int Configs, int Segments);
