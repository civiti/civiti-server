using Microsoft.OpenApi.Models;

namespace Civica.Api.Infrastructure.Configuration;

/// <summary>
/// API versioning configuration for future-proofing
/// </summary>
public static class ApiVersioningConfiguration
{
    /// <summary>
    /// Current API version
    /// </summary>
    public const string CurrentVersion = "v1";
    
    /// <summary>
    /// API version for documentation
    /// </summary>
    public const string ApiVersion = "1.0.0";
    
    /// <summary>
    /// API title for documentation
    /// </summary>
    public const string ApiTitle = "Civica API";
    
    /// <summary>
    /// Gets versioning information for Swagger
    /// </summary>
    public static OpenApiInfo GetApiInfo(string version = CurrentVersion)
    {
        return version switch
        {
            "v1" => new()
            {
                Title = ApiTitle,
                Version = "v1",
                Description = GetV1Description()
            },
            // Future versions can be added here
            _ => throw new NotSupportedException($"API version {version} is not supported")
        };
    }
    
    private static string GetV1Description()
    {
        return """
            ## Overview
            The Civica API v1 provides comprehensive endpoints for civic engagement and issue reporting.
            
            ## Key Features
            - User authentication and profile management
            - Civic issue reporting with photo uploads
            - Email campaign coordination
            - Gamification system with points and badges
            - Administrative moderation tools
            
            ## Authentication
            Use JWT Bearer tokens obtained from Supabase Auth.
            
            ## Rate Limiting
            - Standard: 100 req/min
            - Issue creation: 10 req/hour
            - Uploads: 5MB max
            """;
    }
}