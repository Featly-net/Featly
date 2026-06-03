using System.Text.Json;
using AwesomeAssertions;
using Featly.Server.Settings;
using Featly.Server.Webhooks;
using Featly.Storage.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Covers the settings provider's three-layer precedence (ARCHITECTURE.md §15):
/// hardcoded default -> appsettings -> database (DB wins), plus reload picking up
/// a newly-written DB singleton.
/// </summary>
public class FeatlySettingsProviderTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Uses_appsettings_when_no_db_row_and_section_present()
    {
        var store = new InMemoryFeatlyStore();
        var provider = Build(store, sectionPresent: true, configure: o =>
        {
            o.MaxAttempts = 9;
            o.BaseRetryDelay = TimeSpan.FromSeconds(20);
            o.MaxRetryDelay = TimeSpan.FromSeconds(120);
        });

        await provider.ReloadAsync(TestContext.Current.CancellationToken);

        provider.WebhookSource.Should().Be(FeatlySettingsSource.AppSettings);
        provider.Webhook.MaxAttempts.Should().Be(9);
        provider.Webhook.BaseRetryDelaySeconds.Should().Be(20);
        provider.Webhook.MaxRetryDelaySeconds.Should().Be(120);
    }

    [Fact]
    public async Task Falls_back_to_hardcoded_default_when_no_section_and_no_db()
    {
        var store = new InMemoryFeatlyStore();
        var provider = Build(store, sectionPresent: false);

        await provider.ReloadAsync(TestContext.Current.CancellationToken);

        provider.WebhookSource.Should().Be(FeatlySettingsSource.HardcodedDefault);
        provider.Webhook.MaxAttempts.Should().Be(6); // WebhookOptions default
    }

    [Fact]
    public async Task Database_singleton_overrides_appsettings()
    {
        var store = new InMemoryFeatlyStore();
        await WriteDbWebhookAsync(store, new FeatlyWebhookSettings { MaxAttempts = 20, BaseRetryDelaySeconds = 2, MaxRetryDelaySeconds = 300 });

        var provider = Build(store, sectionPresent: true, configure: o => o.MaxAttempts = 9);
        await provider.ReloadAsync(TestContext.Current.CancellationToken);

        provider.WebhookSource.Should().Be(FeatlySettingsSource.Database);
        provider.Webhook.MaxAttempts.Should().Be(20);
        provider.Webhook.BaseRetryDelaySeconds.Should().Be(2);
        provider.Webhook.MaxRetryDelaySeconds.Should().Be(300);
    }

    [Fact]
    public async Task Reload_picks_up_a_newly_written_db_row()
    {
        var store = new InMemoryFeatlyStore();
        var provider = Build(store, sectionPresent: true, configure: o => o.MaxAttempts = 9);
        var ct = TestContext.Current.CancellationToken;

        await provider.ReloadAsync(ct);
        provider.WebhookSource.Should().Be(FeatlySettingsSource.AppSettings);

        await WriteDbWebhookAsync(store, new FeatlyWebhookSettings { MaxAttempts = 15 });
        await provider.ReloadAsync(ct);

        provider.WebhookSource.Should().Be(FeatlySettingsSource.Database);
        provider.Webhook.MaxAttempts.Should().Be(15);
    }

    [Fact]
    public async Task Authorization_defaults_to_open_hardcoded()
    {
        var store = new InMemoryFeatlyStore();
        var provider = Build(store, sectionPresent: false);
        await provider.ReloadAsync(TestContext.Current.CancellationToken);

        provider.AuthorizationSource.Should().Be(FeatlySettingsSource.HardcodedDefault);
        provider.Authorization.AutoProvisionMode.Should().Be(Featly.Server.Authentication.AutoProvisionMode.Open);
    }

    [Fact]
    public async Task Authorization_reads_appsettings_when_set()
    {
        var store = new InMemoryFeatlyStore();
        var provider = Build(store, sectionPresent: false, configureAuthz: o => o.AutoProvisionMode = Featly.Server.Authentication.AutoProvisionMode.Closed);
        await provider.ReloadAsync(TestContext.Current.CancellationToken);

        provider.AuthorizationSource.Should().Be(FeatlySettingsSource.AppSettings);
        provider.Authorization.AutoProvisionMode.Should().Be(Featly.Server.Authentication.AutoProvisionMode.Closed);
    }

    [Fact]
    public async Task Authorization_database_overrides_appsettings()
    {
        var store = new InMemoryFeatlyStore();
        await store.Settings.UpsertAsync(new SystemSetting
        {
            Key = FeatlySettingsKeys.Authorization,
            Payload = JsonSerializer.SerializeToElement(new { autoProvisionMode = "Closed" }),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "test",
        }, CancellationToken.None);

        var provider = Build(store, sectionPresent: false); // appsettings default = Open
        await provider.ReloadAsync(TestContext.Current.CancellationToken);

        provider.AuthorizationSource.Should().Be(FeatlySettingsSource.Database);
        provider.Authorization.AutoProvisionMode.Should().Be(Featly.Server.Authentication.AutoProvisionMode.Closed);
    }

    private static async Task WriteDbWebhookAsync(InMemoryFeatlyStore store, FeatlyWebhookSettings value) =>
        await store.Settings.UpsertAsync(new SystemSetting
        {
            Key = FeatlySettingsKeys.Webhook,
            Payload = JsonSerializer.SerializeToElement(value, s_json),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "test",
        }, CancellationToken.None);

    private static DefaultFeatlySettingsProvider Build(
        InMemoryFeatlyStore store,
        bool sectionPresent,
        Action<WebhookOptions>? configure = null,
        Action<Featly.Server.Authentication.FeatlyAuthorizationOptions>? configureAuthz = null)
    {
        var services = new ServiceCollection();
        var optionsBuilder = services.AddOptions<WebhookOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        var authzBuilder = services.AddOptions<Featly.Server.Authentication.FeatlyAuthorizationOptions>();
        if (configureAuthz is not null)
        {
            authzBuilder.Configure(configureAuthz);
        }
        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<WebhookOptions>>();
        var authzMonitor = sp.GetRequiredService<IOptionsMonitor<Featly.Server.Authentication.FeatlyAuthorizationOptions>>();

        var configData = sectionPresent
            ? new Dictionary<string, string?> { ["Featly:Webhooks:MaxAttempts"] = "9" }
            : [];
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        return new DefaultFeatlySettingsProvider(store, monitor, authzMonitor, configuration);
    }
}
