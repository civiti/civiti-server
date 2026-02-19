# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
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
