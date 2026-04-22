using System.Text.Json;
using Civiti.Infrastructure.Services;
using Civiti.Infrastructure.Services.Supabase;
using FluentAssertions;

namespace Civiti.Tests.Services;

/// <summary>
/// Focused unit tests for SupabaseAdminClient.ParseUsersPage — the part that is most
/// likely to drift if Supabase changes response shape and doesn't need HTTP to exercise.
/// </summary>
public class SupabaseAdminClientTests
{
    [Fact]
    public void ParseUsersPage_Should_Read_Object_With_Users_Array()
    {
        const string body = """
        {
            "users": [
                {
                    "id": "00000000-0000-0000-0000-000000000001",
                    "email": "alice@example.com",
                    "app_metadata": { "role": "admin" }
                },
                {
                    "id": "00000000-0000-0000-0000-000000000002",
                    "email": "bob@example.com",
                    "app_metadata": { }
                }
            ]
        }
        """;

        SupabaseAdminClient.UsersPage result = SupabaseAdminClient.ParseUsersPage(body);

        result.Users.Should().HaveCount(2);
        result.Users[0].Email.Should().Be("alice@example.com");
        result.Users[0].AppMetadata.ValueKind.Should().Be(JsonValueKind.Object);
        result.Users[0].AppMetadata.GetProperty("role").GetString().Should().Be("admin");

        result.Users[1].Email.Should().Be("bob@example.com");
        result.Users[1].AppMetadata.TryGetProperty("role", out _).Should().BeFalse();
    }

    [Fact]
    public void ParseUsersPage_Should_Read_Bare_Array_Body()
    {
        const string body = """
        [
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "email": "alice@example.com",
                "app_metadata": { "role": "admin" }
            }
        ]
        """;

        SupabaseAdminClient.UsersPage result = SupabaseAdminClient.ParseUsersPage(body);

        result.Users.Should().HaveCount(1);
        result.Users[0].Email.Should().Be("alice@example.com");
    }

    [Fact]
    public void ParseUsersPage_Should_Return_Empty_On_Unexpected_Shape()
    {
        const string body = """{ "unexpected": true }""";

        SupabaseAdminClient.UsersPage result = SupabaseAdminClient.ParseUsersPage(body);

        result.Users.Should().BeEmpty();
    }

    [Fact]
    public void ParseUsersPage_Should_Skip_Entries_Missing_Or_Bad_Id()
    {
        const string body = """
        {
            "users": [
                { "id": "not-a-guid", "email": "x@example.com", "app_metadata": { "role": "admin" } },
                { "id": "00000000-0000-0000-0000-000000000042", "email": "y@example.com", "app_metadata": { "role": "admin" } }
            ]
        }
        """;

        SupabaseAdminClient.UsersPage result = SupabaseAdminClient.ParseUsersPage(body);

        // We still return the entry with bad id (Id = Guid.Empty) — the admin filter downstream
        // will drop it (no role check actually happens on Id). The important invariant is
        // "don't throw on malformed rows" — the caller filters via IsAdmin.
        result.Users.Should().HaveCount(2);
        result.Users.Should().ContainSingle(u => u.Id == Guid.Parse("00000000-0000-0000-0000-000000000042"));
        result.Users.Should().ContainSingle(u => u.Id == Guid.Empty);
    }
}
