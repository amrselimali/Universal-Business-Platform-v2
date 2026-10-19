FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props UniversalBusinessPlatform.slnx ./
COPY src/Ubp.Core/Ubp.Core.csproj src/Ubp.Core/
COPY src/Ubp.Infrastructure/Ubp.Infrastructure.csproj src/Ubp.Infrastructure/
COPY src/Ubp.Application/Ubp.Application.csproj src/Ubp.Application/
COPY src/Ubp.Web/Ubp.Web.csproj src/Ubp.Web/
COPY tests/Ubp.Tests/Ubp.Tests.csproj tests/Ubp.Tests/
RUN dotnet restore UniversalBusinessPlatform.slnx

COPY . .
RUN dotnet publish src/Ubp.Web/Ubp.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Ubp.Web.dll"]
