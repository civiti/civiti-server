# Build context is the repo root (see Civiti.Mcp/railway.json).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so layer caching wins on source-only changes.
# Civiti.Mcp transitively pulls the three shared libraries; all four csprojs are needed for restore.
COPY ["Civiti.Domain/Civiti.Domain.csproj", "Civiti.Domain/"]
COPY ["Civiti.Application/Civiti.Application.csproj", "Civiti.Application/"]
COPY ["Civiti.Infrastructure/Civiti.Infrastructure.csproj", "Civiti.Infrastructure/"]
COPY ["Civiti.Mcp/Civiti.Mcp.csproj", "Civiti.Mcp/"]
RUN dotnet restore "Civiti.Mcp/Civiti.Mcp.csproj"

# Copy the full source tree and publish.
# No test stage: tests live in Civiti.Tests and target the shared libraries; they run in the
# Civiti.Api deploy pipeline (see root Dockerfile). Running them here would just double the
# build time for the same signal.
COPY . .
RUN dotnet publish "Civiti.Mcp/Civiti.Mcp.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Railway sets PORT at runtime; Program.cs reads it and binds 0.0.0.0:$PORT.
# EXPOSE is informational only and does not affect Railway routing.
EXPOSE 8081

ENTRYPOINT ["dotnet", "Civiti.Mcp.dll"]
