using System.ComponentModel;
using Civiti.Application.Localization;
using ModelContextProtocol.Server;

namespace Civiti.Mcp.Tools;

/// <summary>
/// §1 Public static-data tools. Today just the issue-category enum with Romanian labels.
/// Backed directly by <see cref="CategoryLocalization"/>; no service wrapper until a second
/// static-data tool shows up (see tool-inventory.md §9 decision log, 2026-04-23).
/// </summary>
[McpServerToolType]
public sealed class PublicStaticDataTools
{
    [McpServerTool(Name = "get_categories")]
    [Description("Return the list of issue categories with their Romanian labels.")]
    public static object GetCategories() => new { items = CategoryLocalization.GetAll() };
}
