FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first so restore is cached until a csproj changes.
COPY src/Shared/SmartAppointments.BuildingBlocks/SmartAppointments.BuildingBlocks.csproj src/Shared/SmartAppointments.BuildingBlocks/
COPY src/Shared/SmartAppointments.BuildingBlocks.Web/SmartAppointments.BuildingBlocks.Web.csproj src/Shared/SmartAppointments.BuildingBlocks.Web/
COPY src/Services/Auth/Auth.Domain/Auth.Domain.csproj src/Services/Auth/Auth.Domain/
COPY src/Services/Auth/Auth.Application/Auth.Application.csproj src/Services/Auth/Auth.Application/
COPY src/Services/Auth/Auth.Infrastructure/Auth.Infrastructure.csproj src/Services/Auth/Auth.Infrastructure/
COPY src/Services/Auth/Auth.Api/Auth.Api.csproj src/Services/Auth/Auth.Api/
RUN dotnet restore src/Services/Auth/Auth.Api/Auth.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/Services/Auth/ src/Services/Auth/
RUN dotnet publish src/Services/Auth/Auth.Api/Auth.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Auth.Api.dll"]
