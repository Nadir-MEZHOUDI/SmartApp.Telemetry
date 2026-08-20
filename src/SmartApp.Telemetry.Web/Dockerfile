FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY . .
RUN dotnet restore "src/SmartApp.Telemetry.Web/SmartApp.Telemetry.Web.csproj"
RUN dotnet publish "src/SmartApp.Telemetry.Web/SmartApp.Telemetry.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
RUN useradd --system --uid 1000 --create-home --shell /usr/sbin/nologin telemetry
USER telemetry
COPY --from=build --chown=telemetry:telemetry /app/publish .
ENTRYPOINT ["dotnet", "SmartApp.Telemetry.Web.dll"]
