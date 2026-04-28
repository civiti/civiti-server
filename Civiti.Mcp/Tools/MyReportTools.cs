using System.ComponentModel;
using Civiti.Application.Requests.Reports;
using Civiti.Application.Services;
using Civiti.Domain.Entities;
using Civiti.Mcp.Authorization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §2.2 write tool for moderation-style content reports. See Civiti.Mcp/docs/tool-inventory.md.
/// </summary>
[McpServerToolType]
public sealed class MyReportTools(IReportService reports, IMcpCitizenContext citizenContext)
{
    private static readonly HashSet<string> AllowedTargetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "issue", "comment"
    };

    // Built from Enum.GetNames so a new ReportReason value (e.g. Violence) is automatically
    // accepted here without a parallel update — the service-side CreateReportRequest.Validate()
    // does the canonical enum-name check anyway, this set just gives the agent a faster, more
    // specific error than the validator's full enum dump.
    private static readonly HashSet<string> AllowedReportReasons =
        new(Enum.GetNames<ReportReason>(), StringComparer.OrdinalIgnoreCase);

    [McpServerTool(Name = "report_content")]
    [Description("File a moderation report against an issue or comment. targetType='issue' mirrors POST /api/issues/{id}/reports; targetType='comment' mirrors POST /api/comments/{id}/reports. Requires civiti.write scope.")]
    public async Task<object> ReportContent(
        [Description("'issue' or 'comment' — what kind of content the report targets.")] string targetType,
        [Description("Identifier (Guid) of the issue or comment being reported.")] Guid targetId,
        [Description("Reason: Spam | Harassment | Inappropriate | Misinformation | Other.")] string reason,
        [Description("Optional free-text details (max 500 characters) describing why the content was reported.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await citizenContext.RequireCitizenWriteAsync(cancellationToken);
        if (!auth.Authorized)
        {
            return auth.ErrorPayload;
        }

        if (string.IsNullOrWhiteSpace(targetType) || !AllowedTargetTypes.Contains(targetType))
        {
            return new { ok = false, reason = "invalid_input", message = $"targetType must be one of: {string.Join(", ", AllowedTargetTypes)}." };
        }
        if (string.IsNullOrWhiteSpace(reason) || !AllowedReportReasons.Contains(reason))
        {
            return new { ok = false, reason = "invalid_input", message = $"reason must be one of: {string.Join(", ", AllowedReportReasons)}." };
        }

        var request = new CreateReportRequest
        {
            Reason = reason,
            Details = notes
        };
        var result = string.Equals(targetType, "issue", StringComparison.OrdinalIgnoreCase)
            ? await reports.ReportIssueAsync(targetId, request, auth.Context.SupabaseUserId)
            : await reports.ReportCommentAsync(targetId, request, auth.Context.SupabaseUserId);
        if (!result.Success)
        {
            return new { ok = false, reason = "service_error", message = result.Error ?? "Report submission failed." };
        }
        return new { ok = true, reportId = result.ReportId, targetType = targetType.ToLowerInvariant(), targetId };
    }
}
