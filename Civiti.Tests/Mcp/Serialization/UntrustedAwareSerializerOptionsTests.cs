using System.Text.Json;
using System.Text.RegularExpressions;
using Civiti.Application.Responses.Activity;
using Civiti.Application.Responses.Comments;
using Civiti.Application.Responses.Issues;
using Civiti.Domain.Entities;
using Civiti.Mcp.Serialization;
using FluentAssertions;

namespace Civiti.Tests.Mcp.Serialization;

public class UntrustedAwareSerializerOptionsTests
{
    private static readonly JsonSerializerOptions Options = UntrustedAwareSerializerOptions.Build();

    [Fact]
    public void TaggedStringProperty_Serializes_As_QuarantineEnvelope()
    {
        var dto = new IssueListResponse
        {
            Id = Guid.NewGuid(),
            Title = "Pothole on Str. Test",
            Description = "Big and dangerous",
            Address = "Str. Test 1",
            Category = IssueCategory.Infrastructure,
            Status = IssueStatus.Active
        };

        var json = JsonSerializer.Serialize(dto, Options);

        using var doc = JsonDocument.Parse(json);
        var titleProperty = doc.RootElement.GetProperty("title");
        titleProperty.ValueKind.Should().Be(JsonValueKind.Object);
        titleProperty.GetProperty("untrusted").GetBoolean().Should().BeTrue();
        titleProperty.GetProperty("source").GetString().Should().Be("user_supplied");
        titleProperty.GetProperty("value").GetString()
            .Should().MatchRegex("^<untrusted-user-content nonce=\"([0-9A-F]{16})\">Pothole on Str\\. Test</untrusted-user-content nonce=\"\\1\">$",
                "the nonce stamped on the closing tag must equal the one on the opening tag — that's the break-out defense");
    }

    [Fact]
    public void UntaggedStringProperty_Serializes_As_RawString()
    {
        // Civiti.Domain.Entities.IssueStatus is an enum (already not a string), so the
        // useful sentinel here is District/MainPhotoUrl on IssueListResponse, which carry
        // user-input strings that the security review left untagged on purpose (location
        // metadata, not free-text prose). Confirm those still emit raw values.
        var dto = new IssueListResponse
        {
            Id = Guid.NewGuid(),
            Title = "x",
            Description = "y",
            Address = "z",
            District = "Sector 1",
            MainPhotoUrl = "https://example.com/photo.jpg",
            Category = IssueCategory.Infrastructure,
            Status = IssueStatus.Active
        };

        var json = JsonSerializer.Serialize(dto, Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("district").ValueKind.Should().Be(JsonValueKind.String);
        doc.RootElement.GetProperty("district").GetString().Should().Be("Sector 1");
        doc.RootElement.GetProperty("mainPhotoUrl").GetString().Should().Be("https://example.com/photo.jpg");
    }

    [Fact]
    public void NullTaggedProperty_Serializes_As_Null_NotEnvelope()
    {
        // The MCP defaults set JsonIgnoreCondition.WhenWritingNull, so null properties drop
        // out of the JSON entirely. Confirm we don't accidentally emit an envelope around a
        // null value.
        var dto = new IssueDetailResponse
        {
            Id = Guid.NewGuid(),
            Title = "x",
            Description = "y",
            Address = "z",
            Category = IssueCategory.Infrastructure,
            Status = IssueStatus.Active,
            DesiredOutcome = null,
            CommunityImpact = null,
            User = new UserBasicResponse { Id = Guid.NewGuid(), Name = "Anon" }
        };

        var json = JsonSerializer.Serialize(dto, Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("desiredOutcome", out _).Should().BeFalse(
            "JsonIgnoreCondition.WhenWritingNull drops null properties from the output");
        doc.RootElement.TryGetProperty("communityImpact", out _).Should().BeFalse();
    }

    [Fact]
    public void NestedDtos_AreWrappedRecursively()
    {
        // CommentResponse → CommentUserResponse: both sit on the same response payload, so
        // both [Untrusted] fields must be wrapped in one serialization pass.
        var dto = new CommentResponse
        {
            Id = Guid.NewGuid(),
            IssueId = Guid.NewGuid(),
            Content = "Reasonable comment",
            User = new CommentUserResponse
            {
                Id = Guid.NewGuid(),
                DisplayName = "Some User",
                Level = 1
            }
        };

        var json = JsonSerializer.Serialize(dto, Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("content").GetProperty("untrusted").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("user").GetProperty("displayName")
            .GetProperty("untrusted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Nonce_Is_Per_Property_And_Not_Reused_Across_Fields()
    {
        // The matching nonce stamped on BOTH the opening and closing tags is the break-out
        // defense — an attacker who plants hostile content in the DB can't terminate the
        // envelope early because they'd need to know the nonce generated at read time both
        // to craft the close and to make it match the open. If two fields shared a nonce in
        // one serialization pass, an attacker who could observe one field's wrapper could
        // craft content for the other field. Per-property keeps that surface closed.
        var dto = new IssueDetailResponse
        {
            Id = Guid.NewGuid(),
            Title = "title text",
            Description = "description text",
            Address = "address text",
            Category = IssueCategory.Infrastructure,
            Status = IssueStatus.Active,
            User = new UserBasicResponse { Id = Guid.NewGuid(), Name = "Author" }
        };

        var json = JsonSerializer.Serialize(dto, Options);

        // Walk the parsed JSON tree (sidestepping JSON-escape pitfalls in the raw string)
        // and pull the nonce out of each envelope's "value" field. Title, Description,
        // Address, and User.Name are tagged → 4 wrapped fields → 4 distinct nonces.
        using var doc = JsonDocument.Parse(json);
        var nonces = new List<string>();
        CollectNonces(doc.RootElement, nonces);

        nonces.Should().HaveCountGreaterThan(1);
        nonces.Distinct().Should().HaveCount(nonces.Count,
            "every wrapped field gets its own randomly-generated nonce");
    }

    private static void CollectNonces(JsonElement element, List<string> output)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("untrusted", out var untrustedProp)
                && untrustedProp.ValueKind == JsonValueKind.True
                && element.TryGetProperty("value", out var valueProp)
                && valueProp.ValueKind == JsonValueKind.String)
            {
                var match = Regex.Match(valueProp.GetString() ?? string.Empty,
                    "nonce=\"([0-9A-F]{16})\"");
                if (match.Success)
                {
                    output.Add(match.Groups[1].Value);
                }
            }
            foreach (var prop in element.EnumerateObject())
            {
                CollectNonces(prop.Value, output);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectNonces(item, output);
            }
        }
    }

    [Fact]
    public void ClosingTag_Carries_SameNonce_AsOpeningTag()
    {
        // Regression-locks the P1 break-out fix from PR #144 review: the closing tag must
        // carry the same nonce as the opening tag. Without it, an attacker who stores the
        // bare literal "</untrusted-user-content>" in their content terminates the envelope
        // early and injects instructions outside the boundary the LLM is told to honor.
        var dto = new IssueListResponse
        {
            Id = Guid.NewGuid(),
            Title = "Pothole on Str. Test",
            Description = "Big",
            Address = "addr",
            Category = IssueCategory.Infrastructure,
            Status = IssueStatus.Active
        };

        var json = JsonSerializer.Serialize(dto, Options);
        using var doc = JsonDocument.Parse(json);
        var envelope = doc.RootElement.GetProperty("title").GetProperty("value").GetString() ?? string.Empty;

        var openMatch = Regex.Match(envelope, "^<untrusted-user-content nonce=\"([0-9A-F]{16})\">");
        var closeMatch = Regex.Match(envelope, "</untrusted-user-content nonce=\"([0-9A-F]{16})\">$");
        openMatch.Success.Should().BeTrue("opening tag must carry the nonce attribute");
        closeMatch.Success.Should().BeTrue("closing tag must carry the nonce attribute");
        closeMatch.Groups[1].Value.Should().Be(openMatch.Groups[1].Value,
            "the close-tag nonce must match the open-tag nonce so a literal `</untrusted-user-content>` an attacker plants in their content does not match either tag");
    }

    [Fact]
    public void Round_Trip_Read_Of_TaggedProperty_Throws()
    {
        // The converter is write-only by design. Confirm read attempts fail loudly so a
        // future change that tries to deserialize a tool result through these DTOs trips
        // immediately rather than silently dropping the envelope.
        var json = """
            {
              "id": "00000000-0000-0000-0000-000000000000",
              "title": {
                "value": "<untrusted-user-content nonce=\"AAAAAAAAAAAAAAAA\">x</untrusted-user-content>",
                "untrusted": true,
                "source": "user_supplied"
              },
              "description": "",
              "category": "infrastructure",
              "address": "",
              "urgency": "medium",
              "emailsSent": 0,
              "communityVotes": 0,
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z",
              "latitude": 0,
              "longitude": 0,
              "status": "active"
            }
            """;

        var act = () => JsonSerializer.Deserialize<IssueListResponse>(json, Options);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ActivityResponse_Wraps_All_Three_Untrusted_Fields()
    {
        // ActivityResponse is the worst-case surface noted in the security review:
        // IssueTitle, ActorDisplayName, AND a Romanian Message string built by interpolating
        // the title into prose. All three need the wrap.
        var dto = new ActivityResponse
        {
            Id = Guid.NewGuid(),
            Type = ActivityType.NewComment,
            IssueId = Guid.NewGuid(),
            IssueTitle = "Pothole on Str. Test",
            Message = "Cineva a comentat pe issue-ul tău: Pothole on Str. Test",
            ActorDisplayName = "Other User",
            AggregatedCount = 1,
            CreatedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(dto, Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("issueTitle").GetProperty("untrusted").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("message").GetProperty("untrusted").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("actorDisplayName").GetProperty("untrusted").GetBoolean().Should().BeTrue();
    }
}
