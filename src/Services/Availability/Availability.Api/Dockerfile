FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first so restore is cached until a csproj changes.
COPY src/Shared/SmartAppointments.BuildingBlocks/SmartAppointments.BuildingBlocks.csproj src/Shared/SmartAppointments.BuildingBlocks/
COPY src/Shared/SmartAppointments.BuildingBlocks.Web/SmartAppointments.BuildingBlocks.Web.csproj src/Shared/SmartAppointments.BuildingBlocks.Web/
COPY src/Services/Availability/Availability.Domain/Availability.Domain.csproj src/Services/Availability/Availability.Domain/
COPY src/Services/Availability/Availability.Application/Availability.Application.csproj src/Services/Availability/Availability.Application/
COPY src/Services/Availability/Availability.Infrastructure/Availability.Infrastructure.csproj src/Services/Availability/Availability.Infrastructure/
COPY src/Services/Availability/Availability.Api/Availability.Api.csproj src/Services/Availability/Availability.Api/
RUN dotnet restore src/Services/Availability/Availability.Api/Availability.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/Services/Availability/ src/Services/Availability/
RUN dotnet publish src/Services/Availability/Availability.Api/Availability.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Availability.Api.dll"]
