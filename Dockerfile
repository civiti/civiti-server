# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Civica.Api/Civica.Api.csproj", "Civica.Api/"]
RUN dotnet restore "Civica.Api/Civica.Api.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Civica.Api"
RUN dotnet build "Civica.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Civica.Api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Railway will set PORT environment variable
# Our app reads it in Program.cs
EXPOSE 8080

ENTRYPOINT ["dotnet", "Civica.Api.dll"]