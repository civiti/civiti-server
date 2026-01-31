# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Civiti.Api/Civiti.Api.csproj", "Civiti.Api/"]
RUN dotnet restore "Civiti.Api/Civiti.Api.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Civiti.Api"
RUN dotnet build "Civiti.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Civiti.Api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Railway will set PORT environment variable
# Our app reads it in Program.cs
EXPOSE 8080

ENTRYPOINT ["dotnet", "Civiti.Api.dll"]