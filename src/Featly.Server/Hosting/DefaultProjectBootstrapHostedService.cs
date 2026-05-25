using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Hosting;

/// <summary>
/// Ensures a default <see cref="Project"/> and <see cref="Environment"/> exist
/// before the server accepts traffic. Honors the Hangfire-style zero-friction
/// quickstart: the operator wires the host, presses run, and Featly is usable.
/// </summary>
/// <remarks>
/// Skipped when <see cref="FeatlyServerOptions.AutoCreateDefaultProject"/> is false.
/// </remarks>
internal sealed partial class DefaultProjectBootstrapHostedService(
    IOptions<FeatlyServerOptions> options,
    StorageFacade store,
    IHostEnvironment hostEnvironment,
    ILogger<DefaultProjectBootstrapHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.AutoCreateDefaultProject)
        {
            LogSkippingBootstrap(logger);
            return;
        }

        var existingProject = await store.Projects.GetByKeyAsync(opts.DefaultProjectKey, cancellationToken).ConfigureAwait(false);
        Project project;
        if (existingProject is null)
        {
            project = new Project
            {
                Id = Guid.NewGuid(),
                Key = opts.DefaultProjectKey,
                Name = hostEnvironment.ApplicationName,
                Description = "Auto-created default project.",
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await store.Projects.CreateAsync(project, cancellationToken).ConfigureAwait(false);
            LogCreatedProject(logger, project.Key, project.Id);
        }
        else
        {
            project = existingProject;
        }

        var envKey = !string.IsNullOrWhiteSpace(opts.DefaultEnvironmentKey)
            ? opts.DefaultEnvironmentKey
            : hostEnvironment.EnvironmentName.ToLowerInvariant();

        var existingEnv = await store.Environments.GetByKeyAsync(project.Id, envKey, cancellationToken).ConfigureAwait(false);
        if (existingEnv is null)
        {
            var env = new Environment
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Key = envKey,
                Name = envKey,
                IsDefault = true,
                ReadOnly = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await store.Environments.CreateAsync(env, cancellationToken).ConfigureAwait(false);
            LogCreatedEnvironment(logger, env.Key, env.Id, project.Key);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "AutoCreateDefaultProject is disabled; skipping bootstrap.")]
    private static partial void LogSkippingBootstrap(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Created default project '{Key}' ({Id}).")]
    private static partial void LogCreatedProject(ILogger logger, string key, Guid id);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Created default environment '{Key}' ({Id}) in project '{ProjectKey}'.")]
    private static partial void LogCreatedEnvironment(ILogger logger, string key, Guid id, string projectKey);
}
