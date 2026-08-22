# Runtime base
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Use the non-root application UID provided by the runtime image.
ARG APP_UID=1000
USER ${APP_UID}

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["src/SmartApp.Telemetry.Web/SmartApp.Telemetry.Web.csproj", "src/SmartApp.Telemetry.Web/"]
COPY ["src/SmartApp.Telemetry.Infrastructure/SmartApp.Telemetry.Infrastructure.csproj", "src/SmartApp.Telemetry.Infrastructure/"]
COPY ["src/SmartApp.Telemetry.Core/SmartApp.Telemetry.Core.csproj", "src/SmartApp.Telemetry.Core/"]
RUN dotnet restore "./src/SmartApp.Telemetry.Web/SmartApp.Telemetry.Web.csproj"
COPY . .

WORKDIR "/src/src/SmartApp.Telemetry.Web"
RUN dotnet build "./SmartApp.Telemetry.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SmartApp.Telemetry.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartApp.Telemetry.Web.dll"]
