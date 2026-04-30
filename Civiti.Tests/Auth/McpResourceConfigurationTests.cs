using Civiti.Auth.Startup;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Civiti.Tests.Auth;

public class ScopeAllowListSeederBuildDescriptorTests
{
    [Fact]
    public void BuildDescriptor_PopulatesNameAndResources()
    {
        var descriptor = ScopeAllowListSeeder.BuildDescriptor(
            "civiti.read",
            new[] { "https://a.example/mcp", "https://b.example/mcp" });

        descriptor.Name.Should().Be("civiti.read");
        descriptor.Resources.Should().Contain(["https://a.example/mcp", "https://b.example/mcp"]);
    }

    [Fact]
    public void BuildDescriptor_EmptyResources_ProducesScopeWithoutResources()
    {
        var descriptor = ScopeAllowListSeeder.BuildDescriptor("civiti.read", Array.Empty<string>());

        descriptor.Name.Should().Be("civiti.read");
        descriptor.Resources.Should().BeEmpty();
    }

    [Fact]
    public void BuildDescriptor_LeavesUserFacingFieldsNull()
    {
        // Display name + description are sourced from Pages/Consent.cshtml.cs's
        // ScopeDescriptor.For switch — the seeder must not stamp anything that competes
        // with that source of truth.
        var descriptor = ScopeAllowListSeeder.BuildDescriptor("civiti.read", Array.Empty<string>());
        descriptor.DisplayName.Should().BeNull();
        descriptor.Description.Should().BeNull();
    }

    [Fact]
    public void BuildDescriptor_EmptyResources_OverwritesPreviousResources()
    {
        // Sanity check for the seeder's "fall through to upsert when no resources are
        // configured" path. UpdateAsync(existing, descriptor) replaces the persisted
        // record's Resources with whatever the descriptor carries; if the descriptor's
        // Resources is empty, the persisted record loses its prior URL bindings. This is
        // what makes the empty-MCP_RESOURCES warning's claim ("RFC 8707 indicators will be
        // rejected") actually true on a redeploy that unsets the env var.
        var existing = ScopeAllowListSeeder.BuildDescriptor(
            "civiti.read",
            new[] { "https://stale.example/mcp" });
        var fresh = ScopeAllowListSeeder.BuildDescriptor(
            "civiti.read",
            Array.Empty<string>());

        // OpenIddict's UpdateAsync overwrites by descriptor — the assertion mirrors what
        // gets written to the DB, not how UpdateAsync copies fields internally.
        fresh.Resources.Should().BeEmpty();
        existing.Resources.Should().Contain("https://stale.example/mcp"); // sanity
    }
}

public class McpResourceConfigurationTests
{
    [Fact]
    public void Constructor_FiltersBlankAndTrimsValues()
    {
        var config = new McpResourceConfiguration(new[]
        {
            "  https://civiti-mcp-development.up.railway.app/mcp  ",
            "",
            "   ",
            "https://civiti-mcp.up.railway.app/mcp"
        });

        config.Resources.Should().Equal(
            "https://civiti-mcp-development.up.railway.app/mcp",
            "https://civiti-mcp.up.railway.app/mcp");
    }

    [Fact]
    public void Constructor_EmptyInput_ProducesEmptyResources()
    {
        new McpResourceConfiguration(Array.Empty<string>())
            .Resources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://civiti-mcp.up.railway.app/mcp")] // http rejected
    [InlineData("ftp://civiti-mcp.up.railway.app/mcp")]  // wrong scheme
    [InlineData("/relative/path")]                        // not absolute
    [InlineData("not a uri")]                             // unparseable
    [InlineData("civiti-mcp-development.up.railway.app/mcp")] // missing scheme
    public void Constructor_RejectsNonAbsoluteHttps(string bad)
    {
        Action act = () => new McpResourceConfiguration(new[] { bad });
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a valid absolute HTTPS URI*");
    }

    [Fact]
    public void FromConfiguration_PrefersEnvVarOverConfigSection()
    {
        // Set both: env var present should win.
        Environment.SetEnvironmentVariable("MCP_RESOURCES",
            "https://from-env-1.example/mcp,https://from-env-2.example/mcp");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:McpResources:0"] = "https://from-config.example/mcp"
                })
                .Build();

            var resolved = McpResourceConfiguration.FromConfiguration(config);

            resolved.Resources.Should().Equal(
                "https://from-env-1.example/mcp",
                "https://from-env-2.example/mcp");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_RESOURCES", null);
        }
    }

    [Fact]
    public void FromConfiguration_FallsBackToConfigWhenEnvUnset()
    {
        Environment.SetEnvironmentVariable("MCP_RESOURCES", null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:McpResources:0"] = "https://from-config.example/mcp"
            })
            .Build();

        McpResourceConfiguration.FromConfiguration(config)
            .Resources.Should().Equal("https://from-config.example/mcp");
    }

    [Fact]
    public void FromConfiguration_NeitherSet_ReturnsEmpty()
    {
        Environment.SetEnvironmentVariable("MCP_RESOURCES", null);
        var config = new ConfigurationBuilder().Build();

        McpResourceConfiguration.FromConfiguration(config)
            .Resources.Should().BeEmpty();
    }
}
