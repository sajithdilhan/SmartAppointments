FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first so restore is cached until a csproj changes.
COPY src/Shared/SmartAppointments.BuildingBlocks/SmartAppointments.BuildingBlocks.csproj src/Shared/SmartAppointments.BuildingBlocks/
COPY src/Shared/SmartAppointments.BuildingBlocks.Web/SmartAppointments.BuildingBlocks.Web.csproj src/Shared/SmartAppointments.BuildingBlocks.Web/
COPY src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.csproj src/ApiGateway/SmartAppointments.Gateway/
RUN dotnet restore src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.csproj
COPY src/Shared/ src/Shared/
COPY src/ApiGateway/SmartAppointments.Gateway/ src/ApiGateway/SmartAppointments.Gateway/
RUN dotnet publish src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartAppointments.Gateway.dll"]
