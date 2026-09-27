FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/dotnet/FanHub.AnalyticsService/FanHub.AnalyticsService.csproj services/dotnet/FanHub.AnalyticsService/
COPY services/dotnet/shared/FanHub.Shared.Contracts/FanHub.Shared.Contracts.csproj services/dotnet/shared/FanHub.Shared.Contracts/
COPY services/dotnet/shared/FanHub.Shared.Common/FanHub.Shared.Common.csproj services/dotnet/shared/FanHub.Shared.Common/
RUN dotnet restore services/dotnet/FanHub.AnalyticsService/FanHub.AnalyticsService.csproj
COPY services/dotnet/FanHub.AnalyticsService/ services/dotnet/FanHub.AnalyticsService/
COPY services/dotnet/shared/FanHub.Shared.Contracts/ services/dotnet/shared/FanHub.Shared.Contracts/
COPY services/dotnet/shared/FanHub.Shared.Common/ services/dotnet/shared/FanHub.Shared.Common/
RUN dotnet publish services/dotnet/FanHub.AnalyticsService/FanHub.AnalyticsService.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FanHub.AnalyticsService.dll"]
