FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/dotnet/FanHub.IdentityService/FanHub.IdentityService.csproj services/dotnet/FanHub.IdentityService/
COPY services/dotnet/shared/FanHub.Shared.Common/FanHub.Shared.Common.csproj services/dotnet/shared/FanHub.Shared.Common/
COPY services/dotnet/shared/FanHub.Shared.Contracts/FanHub.Shared.Contracts.csproj services/dotnet/shared/FanHub.Shared.Contracts/
RUN dotnet restore services/dotnet/FanHub.IdentityService/FanHub.IdentityService.csproj
COPY services/dotnet/FanHub.IdentityService/ services/dotnet/FanHub.IdentityService/
COPY services/dotnet/shared/FanHub.Shared.Common/ services/dotnet/shared/FanHub.Shared.Common/
COPY services/dotnet/shared/FanHub.Shared.Contracts/ services/dotnet/shared/FanHub.Shared.Contracts/
RUN dotnet publish services/dotnet/FanHub.IdentityService/FanHub.IdentityService.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FanHub.IdentityService.dll"]
