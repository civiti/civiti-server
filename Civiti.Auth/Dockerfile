# Build context is the repo root (see Civiti.Auth/railway.json).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so layer caching wins on source-only changes.
COPY ["Civiti.Domain/Civiti.Domain.csproj", "Civiti.Domain/"]
COPY ["Civiti.Application/Civiti.Application.csproj", "Civiti.Application/"]
COPY ["Civiti.Infrastructure/Civiti.Infrastructure.csproj", "Civiti.Infrastructure/"]
COPY ["Civiti.Auth/Civiti.Auth.csproj", "Civiti.Auth/"]
RUN dotnet restore "Civiti.Auth/Civiti.Auth.csproj"

# Copy the full source tree and publish. No test stage: the test suite lives in Civiti.Tests
# and runs in the Civiti.Api deploy pipeline; nothing Civiti.Auth-specific to duplicate here.
COPY . .
RUN dotnet publish "Civiti.Auth/Civiti.Auth.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Railway sets PORT at runtime; Program.cs reads it and binds 0.0.0.0:$PORT.
EXPOSE 8082

ENTRYPOINT ["dotnet", "Civiti.Auth.dll"]
