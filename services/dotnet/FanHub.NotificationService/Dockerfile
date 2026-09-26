FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/dotnet/FanHub.NotificationService/FanHub.NotificationService.csproj services/dotnet/FanHub.NotificationService/
COPY services/dotnet/shared/FanHub.Shared.Contracts/FanHub.Shared.Contracts.csproj services/dotnet/shared/FanHub.Shared.Contracts/
RUN dotnet restore services/dotnet/FanHub.NotificationService/FanHub.NotificationService.csproj
COPY services/dotnet/FanHub.NotificationService/ services/dotnet/FanHub.NotificationService/
COPY services/dotnet/shared/FanHub.Shared.Contracts/ services/dotnet/shared/FanHub.Shared.Contracts/
RUN dotnet publish services/dotnet/FanHub.NotificationService/FanHub.NotificationService.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FanHub.NotificationService.dll"]
