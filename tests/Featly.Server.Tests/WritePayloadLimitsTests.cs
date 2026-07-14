using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Endpoints;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Defense-in-depth caps on admin write payloads (issue #206): unit coverage of
/// the shared validator plus one end-to-end proof that an oversized flag is
/// rejected with 400 before it is stored.
/// </summary>
public class WritePayloadLimitsTests
{
    private const string AdminKey = "admin-key-test";
    private static readonly JsonElement TrueValue = JsonSerializer.SerializeToElement(true);

    private static Variant Variant(string key) => new() { Key = key, Name = key, Value = TrueValue };
    private static Condition PlainCondition() => new() { Attribute = "user.plan", Operator = ConditionOperator.Equals, Value = JsonSerializer.SerializeToElement("pro") };
    private static Condition RegexCondition(string pattern) => new() { Attribute = "user.id", Operator = ConditionOperator.Matches, Value = JsonSerializer.SerializeToElement(pattern) };

    [Fact]
    public void Accepts_reasonable_payloads()
    {
        WritePayloadLimits.ValidateFlag([Variant("on"), Variant("off")], null).Should().BeNull();
        WritePayloadLimits.ValidateConfig(null).Should().BeNull();
        WritePayloadLimits.ValidateConditions([PlainCondition()]).Should().BeNull();
        WritePayloadLimits.ValidateConditions([RegexCondition("^user-[0-9]+$")]).Should().BeNull();
    }

    [Fact]
    public void Rejects_too_many_variants()
    {
        var variants = Enumerable.Range(0, WritePayloadLimits.MaxVariants + 1).Select(i => Variant("v" + i)).ToList();
        WritePayloadLimits.ValidateFlag(variants, null).Should().Contain("variants");
    }

    [Fact]
    public void Rejects_too_many_rules()
    {
        var rules = Enumerable.Range(0, WritePayloadLimits.MaxRules + 1)
            .Select(_ => new Rule { Order = 0, Outcome = new RuleOutcome { VariantKey = "on" } }).ToList();
        WritePayloadLimits.ValidateFlag([Variant("on")], rules).Should().Contain("rules");
    }

    [Fact]
    public void Rejects_too_many_conditions()
    {
        var conditions = Enumerable.Range(0, WritePayloadLimits.MaxConditionsPerRule + 1).Select(_ => PlainCondition()).ToList();
        WritePayloadLimits.ValidateConditions(conditions).Should().Contain("conditions");
    }

    [Fact]
    public void Rejects_an_overlong_regex_pattern()
    {
        var huge = new string('a', WritePayloadLimits.MaxPatternLength + 1);
        WritePayloadLimits.ValidateConditions([RegexCondition(huge)]).Should().Contain("pattern");
    }

    [Fact]
    public async Task Create_flag_with_too_many_variants_is_rejected()
    {
        using var host = await BuildHostAsync();
        var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        var variants = Enumerable.Range(0, WritePayloadLimits.MaxVariants + 1)
            .Select(i => new { key = "v" + i, name = "V" + i, value = true })
            .ToArray();

        var resp = await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "too-many",
            name = "Too many",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "v0",
            variants,
        }, ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var store = host.Services.GetRequiredService<Featly.Storage.IFeatlyStore>();
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        (await store.Flags.GetAsync(env!.Id, "too-many", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Create_config_with_too_many_rules_is_rejected()
    {
        using var host = await BuildHostAsync();
        var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        var rules = Enumerable.Range(0, WritePayloadLimits.MaxRules + 1)
            .Select(_ => new { order = 0, value = 1, conditions = Array.Empty<object>() })
            .ToArray();

        var resp = await admin.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "too-many-rules",
            name = "Too many",
            type = "Int",
            defaultValue = 0,
            rules,
        }, ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_segment_with_too_many_conditions_is_rejected()
    {
        using var host = await BuildHostAsync();
        var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        var conditions = Enumerable.Range(0, WritePayloadLimits.MaxConditionsPerRule + 1)
            .Select(_ => new { attribute = "user.plan", @operator = "Equals", value = "pro" })
            .ToArray();

        var resp = await admin.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "too-many-conditions",
            name = "Too many",
            conditions,
        }, ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                }));
                web.ConfigureServices(services =>
                {
                    services.AddFeatlyInMemoryStore();
                    services.AddFeatlyServer();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapFeatlyApi());
                });
            });
        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }
}
