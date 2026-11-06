# Configuration Guide

This guide explains how configuration works in the Civica API application, particularly for Supabase integration and deployment on Railway.

## Configuration Hierarchy

The application uses a hierarchical configuration system with the following precedence (highest to lowest):

1. **Environment Variables** - Always take precedence
2. **appsettings.{Environment}.json** - Environment-specific settings
3. **appsettings.json** - Base configuration (mostly empty to force env vars)

## Supabase Configuration

### Implementation Details

The application uses dependency injection for Supabase configuration through the `SupabaseConfiguration` class:

```csharp
public class SupabaseConfiguration
{
    public string Url { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
}
```

This configuration is:
- Registered as a singleton in `Program.cs`
- Injected into services that need Supabase access
- Validated at startup (throws exception if not configured)

### Configuration Flow in Program.cs

```csharp
// 1. Try environment variables first, then fall back to appsettings
var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    supabaseUrl = builder.Configuration["Supabase:Url"];
}

// 2. Validate configuration using IsNullOrWhiteSpace (not just null check)
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    throw new InvalidOperationException("Supabase URL not configured. Please set SUPABASE_URL environment variable or configure Supabase:Url in appsettings.json");
}

// 3. Register validated configuration
builder.Services.AddSingleton(new SupabaseConfiguration 
{ 
    Url = supabaseUrl,
    PublishableKey = supabasePublishableKey
});

// 4. Inject into services
public class SupabaseService(ILogger<SupabaseService> logger, SupabaseConfiguration supabaseConfig)
```

**Important**: The validation uses `string.IsNullOrWhiteSpace()` to catch both null and empty string values, ensuring the application fails fast at startup if Supabase is not properly configured.

## Environment Variables

### Required for Production (Railway)

```env
# Database connection (Railway provides this)
DATABASE_URL=postgresql://user:password@host:port/database

# Supabase configuration
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_PUBLISHABLE_KEY=your-publishable-key

# Optional
SUPABASE_SERVICE_KEY=your-service-role-key
PORT=8080  # Railway provides this

# Claude AI (Civiti-Server and Civiti-MCP). The model is fixed in code, not here:
# see docs/technical/ai-models.md.
CLAUDE_API_KEY=your-claude-api-key

# Civiti-Auth only. Required outside Development; see "Civiti-Auth token keys" below.
OIDC_SIGNING_KEY=base64-encoded-pem-rsa-private-key
OIDC_ENCRYPTION_KEY=base64-encoded-pem-rsa-private-key
```

### Local Development

For local development, create a `.env` file (already in .gitignore):

```env
# Local PostgreSQL (via Docker)
DATABASE_URL=Host=localhost;Port=5433;Database=civica_dev;Username=civica;Password=civica123

# Your Supabase project
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_PUBLISHABLE_KEY=your-publishable-key
```

## Configuration Files

### appsettings.json

The base configuration file intentionally has empty values for sensitive settings:

```json
{
  "Supabase": {
    "Url": "",          // Empty to force env var usage
    "PublishableKey": "",      // Empty to force env var usage
    "ServiceRoleKey": ""
  }
}
```

**Why empty strings instead of omitting the keys?**
- Provides clear documentation of required configuration keys
- Prevents NullReferenceException when accessing nested configuration
- Forces explicit validation with `IsNullOrWhiteSpace()` 
- Makes it obvious that these values must come from environment variables

### appsettings.Development.json

Contains development-specific settings like detailed logging:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5433;Database=civica_dev;Username=civica;Password=civica123"
  }
}
```

### appsettings.Production.json

Contains production-specific settings like CORS origins:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://civica.app",
      "https://www.civica.app"
    ]
  }
}
```

## Railway Deployment Configuration

### Setting Environment Variables

1. Go to your Railway project dashboard
2. Click on the Civica service
3. Go to the "Variables" tab
4. Add the required environment variables:
   - `SUPABASE_URL`
   - `SUPABASE_PUBLISHABLE_KEY`
   - `DATABASE_URL` (automatically provided by Railway PostgreSQL)

### Civiti-Auth token keys

Civiti-Auth signs access tokens with `OIDC_SIGNING_KEY` and encrypts refresh tokens with `OIDC_ENCRYPTION_KEY`. Both keys must stay the same across deploys: if either one changes, every connected MCP client (Claude Desktop, Claude Code and so on) has to sign in again.

Both are required on every environment except Development. Civiti-Auth refuses to start if either is missing or invalid, and a failed healthcheck leaves the previous deployment serving. Development can leave both unset and gets throwaway keys that last until the next restart.

Each value is a 2048-bit RSA private key in PEM format, base64-encoded onto one line. Generate each key separately, because the two must differ:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 | base64 -w0
```

Keep these out of git and logs. Anyone holding them can forge tokens for any user. To set them without the value appearing in your shell history or a process list, pipe it in, once per key:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 | base64 -w0 \
  | railway variable set OIDC_SIGNING_KEY --stdin --skip-deploys --service Civiti-Auth --environment production
```

`--skip-deploys` matters the first time: without it, Railway redeploys the service as soon as a variable changes.

To check it worked, look for `OpenIddict signing and encryption keys loaded from configuration.` in the Civiti-Auth startup logs, and confirm the `kid` at `/.well-known/jwks` stays the same across a redeploy.

Replacing a key signs everyone out once, so only rotate on a suspected leak or a schedule you've announced. Civiti-Auth loads one key of each kind, so there's no overlap period.

### Verifying Configuration

The application logs configuration sources at startup:

```
Supabase URL from env: https://xxx.supabase.co, from config: , using: https://xxx.supabase.co
```

The health check endpoint also validates configuration:

```bash
GET /api/health

{
  "status": "Healthy",
  "database": "connected",
  "supabase": "connected"
}
```

## Troubleshooting

### "Supabase URL not configured" Error

This error occurs when:
1. No `SUPABASE_URL` environment variable is set
2. The appsettings.json has an empty value (intentional)

**Solution**: Set the `SUPABASE_URL` environment variable in Railway

### "Invalid DATABASE_URL format" Error

Railway provides PostgreSQL URLs in the format:
```
postgresql://user:password@host:port/database
```

The application automatically converts this to .NET's connection string format.

### "OIDC_SIGNING_KEY is required" Error

Civiti-Auth stopped at startup because a token key is missing, malformed, or shorter than 2048 bits.

**Solution**: Set both keys as described in [Civiti-Auth token keys](#civiti-auth-token-keys). The error names the variable at fault; it never includes the value.

### Configuration Not Loading

Check the startup logs for configuration sources:
- "Using connection string from: DATABASE_URL env var" vs "appsettings"
- "Supabase URL from env: ..." shows what values are being read

## Best Practices

1. **Never commit real credentials** - Use environment variables
2. **Keep appsettings.json generic** - Use empty strings for secrets
3. **Use environment-specific files** - appsettings.Production.json for prod-only settings
4. **Validate at startup** - Fail fast if configuration is missing
5. **Log configuration sources** - But mask sensitive values
6. **Use dependency injection** - Inject configuration classes, not IConfiguration

## Security Notes

- The `SUPABASE_PUBLISHABLE_KEY` is safe for client-side use (formerly called "Anon Key")
- The `SUPABASE_SERVICE_KEY` should never be exposed to clients
- Database passwords are masked in logs using regex patterns
- JWT tokens are validated but never logged in full