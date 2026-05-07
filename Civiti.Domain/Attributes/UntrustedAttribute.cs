namespace Civiti.Domain.Attributes;

/// <summary>
/// Marks a string-typed DTO property as carrying user-supplied (untrusted) content. The
/// MCP serializer (<c>Civiti.Mcp.Serialization.UntrustedStringConverter</c>) wraps every
/// tagged property in a quarantine envelope so the receiving LLM treats it as data, not
/// instructions. The REST API serialization is unaffected — it still emits the raw string.
///
/// <para>
/// Apply to any DTO field whose value originates from user input that another user's session
/// can see: issue title/description/address/desired-outcome/community-impact, comment content,
/// display names, photo descriptions, etc. See
/// <c>docs/security/mcp-prompt-injection-review-2026-05-05.md</c> MED #3 for the threat
/// model and the trust-boundary rule (anything from <c>users.*</c>, <c>comments.*</c>, or
/// <c>issues.title|description|address|desired_outcome|community_impact</c> is untrusted; the
/// curated <c>authorities.*</c> table is trusted).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class UntrustedAttribute : Attribute;
