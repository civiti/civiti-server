using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Swashbuckle.AspNetCore.Filters;

namespace Civica.Api.Infrastructure.Configuration;

/// <summary>
/// Configuration for Swagger/OpenAPI documentation
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>
    /// Configures Swagger generation options
    /// </summary>
    public static void ConfigureSwagger(this SwaggerGenOptions options, IConfiguration configuration)
    {
        // API Info
        options.SwaggerDoc("v1", new()
        {
            Title = "Civica API",
            Version = "v1",
            Description = """
                ## Overview
                The Civica API powers a civic engagement platform that enables Romanian citizens to report local issues and coordinate email campaigns to pressure authorities for resolution.
                
                ## Features
                - **User Authentication**: Secure registration and login via Supabase Auth
                - **Issue Reporting**: Create and track civic issues with photo uploads
                - **Email Campaigns**: Coordinate community pressure through email tracking
                - **Gamification**: Earn points, badges, and achievements for civic participation
                - **Admin Moderation**: Review and approve reported issues
                
                ## Authentication
                This API uses JWT Bearer authentication. Obtain a token through Supabase Auth and include it in the `Authorization` header:
                ```
                Authorization: Bearer YOUR_JWT_TOKEN
                ```
                
                ## Rate Limiting
                - Standard endpoints: 100 requests per minute
                - Issue creation: 10 requests per hour
                - File uploads: 5MB maximum file size
                
                ## Response Codes
                - `200 OK`: Successful request
                - `201 Created`: Resource created successfully
                - `400 Bad Request`: Invalid request parameters
                - `401 Unauthorized`: Missing or invalid authentication
                - `403 Forbidden`: Insufficient permissions
                - `404 Not Found`: Resource not found
                - `429 Too Many Requests`: Rate limit exceeded
                - `500 Internal Server Error`: Server error
                """,
            Contact = new()
            {
                Name = "Civica Support",
                Email = "support@civica.ro",
                Url = new("https://civica.ro/support")
            },
            License = new()
            {
                Name = "MIT License",
                Url = new("https://opensource.org/licenses/MIT")
            },
            TermsOfService = new("https://civica.ro/terms")
        });

        // Security Definition for JWT Bearer
        options.AddSecurityDefinition("Bearer", new()
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = """
                JWT Authorization header using the Bearer scheme.
                
                Enter your token in the text input below.
                
                Example: "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
                """
        });

        // Security Requirement
        options.AddSecurityRequirement(new()
        {
            {
                new()
                {
                    Reference = new()
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        // Add XML Comments
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }

        // Enable annotations
        options.EnableAnnotations();

        // Custom operation filter for better documentation
        options.OperationFilter<SwaggerOperationFilter>();
        
        // Custom schema filter for enhanced model documentation
        options.SchemaFilter<SwaggerSchemaFilter>();

        // Group operations by tags
        options.TagActionsBy(api =>
        {
            // Use the tag from the endpoint if available
            if (api.ActionDescriptor.EndpointMetadata
                .OfType<ITagsMetadata>()
                .FirstOrDefault()?.Tags?.FirstOrDefault() is string tag)
            {
                return [tag];
            }
            
            // Fallback to controller name
            return ["General"];
        });

        // Sort tags alphabetically
        options.OrderActionsBy(api => api.RelativePath);

        // Configure example values
        options.ExampleFilters();

        // Map types for better documentation
        options.MapType<DateOnly>(() => new()
        {
            Type = "string",
            Format = "date",
            Example = new Microsoft.OpenApi.Any.OpenApiString("2024-01-15")
        });

        options.MapType<TimeOnly>(() => new()
        {
            Type = "string",
            Format = "time",
            Example = new Microsoft.OpenApi.Any.OpenApiString("14:30:00")
        });

        // Custom naming strategy for schemas
        options.CustomSchemaIds(type =>
        {
            // Remove generic type parameters from schema names
            var typeName = type.Name;
            if (type.IsGenericType)
            {
                var genericArgs = string.Join("", type.GetGenericArguments().Select(t => t.Name));
                typeName = $"{type.Name.Split('`')[0]}Of{genericArgs}";
            }
            return typeName;
        });
    }
}

/// <summary>
/// Custom operation filter to enhance API documentation
/// </summary>
public class SwaggerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Add default responses if not already defined
        if (!operation.Responses.ContainsKey("401"))
        {
            operation.Responses.Add("401", new()
            {
                Description = "Unauthorized - Invalid or missing authentication token",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = new()
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new()
                                { 
                                    Type = "string",
                                    Example = new Microsoft.OpenApi.Any.OpenApiString("Invalid authentication token")
                                }
                            }
                        }
                    }
                }
            });
        }

        if (!operation.Responses.ContainsKey("500"))
        {
            operation.Responses.Add("500", new()
            {
                Description = "Internal Server Error - An unexpected error occurred",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = new()
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new()
                                { 
                                    Type = "string",
                                    Example = new Microsoft.OpenApi.Any.OpenApiString("An unexpected error occurred")
                                },
                                ["requestId"] = new()
                                { 
                                    Type = "string",
                                    Example = new Microsoft.OpenApi.Any.OpenApiString("abc123-def456")
                                }
                            }
                        }
                    }
                }
            });
        }

        // Add operation ID if not present
        if (string.IsNullOrEmpty(operation.OperationId))
        {
            var httpMethod = context.ApiDescription.HttpMethod?.ToLower() ?? "unknown";
            var path = context.ApiDescription.RelativePath?.Replace("/", "_").Replace("{", "").Replace("}", "") ?? "unknown";
            operation.OperationId = $"{httpMethod}_{path}";
        }

        // Enhance parameter descriptions
        foreach (OpenApiParameter? parameter in operation.Parameters)
        {
            if (parameter.In == ParameterLocation.Query && string.IsNullOrEmpty(parameter.Description))
            {
                parameter.Description = parameter.Name switch
                {
                    "page" => "Page number for pagination (default: 1)",
                    "pageSize" => "Number of items per page (default: 12, max: 100)",
                    "sortBy" => "Field to sort by",
                    "sortDescending" => "Sort in descending order (default: true)",
                    "category" => "Filter by issue category",
                    "urgency" => "Filter by urgency level",
                    "district" => "Filter by district",
                    "status" => "Filter by issue status",
                    _ => parameter.Description
                };
            }
        }

        // Add rate limiting information to summary
        if (operation.Tags?.Any(t => t.Name == "Issues") == true && 
            context.ApiDescription.HttpMethod == "POST")
        {
            operation.Summary += " (Rate limited: 10 requests per hour)";
        }
    }
}

/// <summary>
/// Custom schema filter to enhance model documentation
/// </summary>
public class SwaggerSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        // Add descriptions for common properties
        if (schema.Properties != null)
        {
            foreach (KeyValuePair<string, OpenApiSchema> property in schema.Properties)
            {
                if (string.IsNullOrEmpty(property.Value.Description))
                {
                    property.Value.Description = property.Key switch
                    {
                        "id" => "Unique identifier",
                        "createdAt" => "Creation timestamp in UTC",
                        "updatedAt" => "Last update timestamp in UTC",
                        "email" => "Email address",
                        "phoneNumber" => "Phone number in international format",
                        "points" => "Total points earned through gamification",
                        "level" => "User level based on points",
                        "isActive" => "Whether the record is active",
                        "isDeleted" => "Whether the record is soft-deleted",
                        _ => property.Value.Description
                    };
                }

                // Mark required fields
                if (property.Key.EndsWith("Id") || property.Key == "email")
                {
                    property.Value.Nullable = false;
                }
            }
        }

        // Add examples for enums
        if (!context.Type.IsEnum) return;
        schema.Enum = Enum.GetValues(context.Type)
            .Cast<object>()
            .Select(e => new Microsoft.OpenApi.Any.OpenApiString(e.ToString()))
            .ToList<Microsoft.OpenApi.Any.IOpenApiAny>();
            
        schema.Type = "string";
        schema.Description = $"Possible values: {string.Join(", ", Enum.GetNames(context.Type))}";
    }
}