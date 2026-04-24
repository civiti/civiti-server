# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy all csprojs first so `dotnet restore` sees the full ProjectReference graph.
# Restoring with only a subset of csprojs present makes the restorer skip the missing
# ProjectReferences entirely, which leaves the resulting project.assets.json missing the
# transitive PackageReferences of those projects. At test compile time MSBuild then
# copy-locals to Civiti.Tests/bin only what Tests's own assets.json tracks — so package
# DLLs declared in (say) Civiti.Infrastructure never reach Civiti.Tests/bin, and
# tests that load those types fail at runtime with FileNotFoundException.
COPY ["Civiti.Domain/Civiti.Domain.csproj", "Civiti.Domain/"]
COPY ["Civiti.Application/Civiti.Application.csproj", "Civiti.Application/"]
COPY ["Civiti.Infrastructure/Civiti.Infrastructure.csproj", "Civiti.Infrastructure/"]
COPY ["Civiti.Api/Civiti.Api.csproj", "Civiti.Api/"]
COPY ["Civiti.Tests/Civiti.Tests.csproj", "Civiti.Tests/"]
RUN dotnet restore "Civiti.Api/Civiti.Api.csproj"
RUN dotnet restore "Civiti.Tests/Civiti.Tests.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Civiti.Api"
RUN dotnet build "Civiti.Api.csproj" -c Release -o /app/build

# Test stage (fails build if tests fail)
FROM build AS test
WORKDIR /src
RUN dotnet test "Civiti.Tests/Civiti.Tests.csproj" -c Release --no-restore --logger "console;verbosity=normal"

# Publish stage (depends on test passing)
FROM test AS publish
WORKDIR "/src/Civiti.Api"
RUN dotnet publish "Civiti.Api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Railway will set PORT environment variable
# Our app reads it in Program.cs
EXPOSE 8080

ENTRYPOINT ["dotnet", "Civiti.Api.dll"]
