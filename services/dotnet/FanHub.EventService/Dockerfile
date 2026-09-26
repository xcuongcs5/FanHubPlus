FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/dotnet/FanHub.EventService/FanHub.EventService.csproj services/dotnet/FanHub.EventService/
COPY services/dotnet/shared/FanHub.Shared.Contracts/FanHub.Shared.Contracts.csproj services/dotnet/shared/FanHub.Shared.Contracts/
RUN dotnet restore services/dotnet/FanHub.EventService/FanHub.EventService.csproj
COPY services/dotnet/FanHub.EventService/ services/dotnet/FanHub.EventService/
COPY services/dotnet/shared/FanHub.Shared.Contracts/ services/dotnet/shared/FanHub.Shared.Contracts/
RUN dotnet publish services/dotnet/FanHub.EventService/FanHub.EventService.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FanHub.EventService.dll"]
