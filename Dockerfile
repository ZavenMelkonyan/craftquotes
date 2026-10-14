# Build: SDK image. Only the API is published; tests run in CI, not in the image build.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CraftQuotes.Api/CraftQuotes.Api.csproj src/CraftQuotes.Api/
RUN dotnet restore src/CraftQuotes.Api/CraftQuotes.Api.csproj

COPY src/ src/
RUN dotnet publish src/CraftQuotes.Api/CraftQuotes.Api.csproj -c Release --no-restore -o /app

# Runtime: ASP.NET image (Ubuntu-based; .NET 10 has no Debian images), running as the built-in non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CraftQuotes.Api.dll"]
