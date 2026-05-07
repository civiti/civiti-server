using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civiti.Mcp.Serialization;

/// <summary>
/// Property-level <see cref="JsonConverter{T}"/> applied to string fields tagged with
/// <c>[Untrusted]</c>. Wraps the raw value in a quarantine envelope so the receiving LLM
/// treats user-supplied content as data, not instructions:
/// <code>
/// {
///   "value":     "&lt;untrusted-user-content nonce=\"a1b2…\"&gt;raw text&lt;/untrusted-user-content&gt;",
///   "untrusted": true,
///   "source":    "user_supplied"
/// }
/// </code>
///
/// <para>
/// The structured envelope (typed marker + key/value flags) and the inline delimiter
/// together implement OWASP LLM01:2025's "structured separation + delimited blocks" guidance
/// — the architectural equivalent of SQL parameterization for LLM tool results. JSON-string
/// escaping handles the inner-quote case; the per-string random nonce on the closing tag
/// prevents an attacker from breaking out by including the closing literal in their content
/// (they don't know the nonce at write time, so they can't craft a matching close).
/// </para>
///
/// <para>
/// Wired in via <see cref="UntrustedAwareSerializerOptions.Build"/> using
/// <c>JsonTypeInfoResolver.WithAddedModifier</c>, which scans every type's properties for
/// the <c>[Untrusted]</c> attribute and assigns this converter as the property's
/// <c>CustomConverter</c>. That makes the wrap a per-property concern — DTOs simply tag
/// their string fields and the serializer does the rest, so a future maintainer adding a
/// new user-supplied field just types <c>[Untrusted] public string Foo { get; init; }</c>
/// and forgetting to wrap is impossible.
/// </para>
///
/// <para>
/// Applies only on the MCP serialization path. The REST API uses
/// <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions"/> configured separately and emits
/// the raw string, so existing REST consumers (Civiti frontend) see no change.
/// </para>
/// </summary>
internal sealed class UntrustedStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // The MCP framework only invokes property converters in the write direction for tool
        // results; tool args don't go through these DTOs (each tool method declares its own
        // primitive parameters). If a future code path hits this branch — e.g. round-tripping
        // a tool result for a test or sampling-handler conversion — fail loudly so the
        // mismatch isn't silently ignored. Reading an envelope back into a plain string is
        // intentionally not supported because the unwrap is meaningless without the closing-
        // tag verification the LLM is supposed to perform.
        throw new NotSupportedException(
            $"{nameof(UntrustedStringConverter)} is write-only — untrusted-content envelopes "
            + "are not designed to round-trip through the same property converter.");
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        // 8 random bytes → 16 hex chars. Sufficient unguessable surface for a per-string
        // delimiter — an attacker writing content into the DB cannot predict or read the
        // nonce that will be generated when their content is later surfaced.
        Span<byte> nonceBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(nonceBytes);
        string nonce = Convert.ToHexString(nonceBytes);

        writer.WriteStartObject();
        writer.WriteString("value", $"<untrusted-user-content nonce=\"{nonce}\">{value}</untrusted-user-content>");
        writer.WriteBoolean("untrusted", true);
        writer.WriteString("source", "user_supplied");
        writer.WriteEndObject();
    }
}
