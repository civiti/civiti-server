using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Civiti.Domain.Attributes;
using ModelContextProtocol;

namespace Civiti.Mcp.Serialization;

/// <summary>
/// Builds the <see cref="JsonSerializerOptions"/> instance the MCP transport uses for tool
/// result serialization. Layers a property-modifier on top of
/// <see cref="McpJsonUtilities.DefaultOptions"/> that scans every type's properties for
/// <c>[Untrusted]</c> and wires up <see cref="UntrustedStringConverter"/> as the per-property
/// custom converter — see that type's docs for the envelope shape and threat model.
/// </summary>
internal static class UntrustedAwareSerializerOptions
{
    public static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);

        // The MCP defaults already provide a TypeInfoResolver. Layering with WithAddedModifier
        // preserves source-generated contracts the MCP library ships for protocol exchange
        // types and only adds our property-level customization on top — DTOs whose properties
        // never carry [Untrusted] are unaffected, and the modifier is a no-op for them.
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(ApplyUntrustedConverter);

        return options;
    }

    private static readonly UntrustedStringConverter UntrustedStringConverter = new();

    private static void ApplyUntrustedConverter(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType != typeof(string))
            {
                continue;
            }

            var attributes = property.AttributeProvider?.GetCustomAttributes(typeof(UntrustedAttribute), inherit: true);
            if (attributes is null || attributes.Length == 0)
            {
                continue;
            }

            // Per-property converter — System.Text.Json invokes it only when serializing
            // this property of this type, so the envelope wrap is precisely scoped to
            // tagged fields. Untagged string properties on the same DTO emit raw values.
            property.CustomConverter = UntrustedStringConverter;
        }
    }
}
