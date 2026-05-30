using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="Project"/> entities — the
/// top-level scope grouping a set of environments. The dashboard's Projects
/// screen consumes these; the key is immutable, only name/description change.
/// </summary>
internal static class AdminProjectsEndpoints
{
    public static RouteGroupBuilder MapAdminProjects(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/projects").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Projects.List").RequirePermission(Permission.ProjectRead);
        admin.MapGet("/{key}", GetAsync).WithName("Featly.Admin.Projects.Get").RequirePermission(Permission.ProjectRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Projects.Create").RequirePermission(Permission.ProjectCreate);
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Projects.Update").RequirePermission(Permission.ProjectUpdate);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var projects = await store.Projects.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(projects);
    }

    private static async Task<IResult> GetAsync(string key, StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetByKeyAsync(key, ct).ConfigureAwait(false);
        return project is null ? Results.NotFound(new { error = $"Project '{key}' not found." }) : Results.Ok(project);
    }

    private static async Task<IResult> CreateAsync(ProjectWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Key))
        {
            return Results.BadRequest(new { error = "key is required." });
        }

        var existing = await store.Projects.GetByKeyAsync(body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Project '{body.Key}' already exists." });
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Key = body.Key,
            Name = string.IsNullOrWhiteSpace(body.Name) ? body.Key : body.Name,
            Description = body.Description,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Projects.CreateAsync(project, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/projects/{project.Key}", project);
    }

    private static async Task<IResult> UpdateAsync(string key, ProjectWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var existing = await store.Projects.GetByKeyAsync(key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Project '{key}' not found." });
        }

        existing.Name = string.IsNullOrWhiteSpace(body.Name) ? existing.Name : body.Name;
        existing.Description = body.Description;

        await store.Projects.UpdateAsync(existing, ct).ConfigureAwait(false);
        return Results.Ok(existing);
    }
}

/// <summary>Inbound shape for POST / PUT on the admin projects endpoint.</summary>
public sealed record ProjectWriteRequest(
    string Key,
    string? Name,
    string? Description);
