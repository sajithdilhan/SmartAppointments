FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first so restore is cached until a csproj changes.
COPY src/Shared/SmartAppointments.BuildingBlocks/SmartAppointments.BuildingBlocks.csproj src/Shared/SmartAppointments.BuildingBlocks/
COPY src/Shared/SmartAppointments.BuildingBlocks.Web/SmartAppointments.BuildingBlocks.Web.csproj src/Shared/SmartAppointments.BuildingBlocks.Web/
COPY src/Services/Booking/Booking.Domain/Booking.Domain.csproj src/Services/Booking/Booking.Domain/
COPY src/Services/Booking/Booking.Application/Booking.Application.csproj src/Services/Booking/Booking.Application/
COPY src/Services/Booking/Booking.Infrastructure/Booking.Infrastructure.csproj src/Services/Booking/Booking.Infrastructure/
COPY src/Services/Booking/Booking.Api/Booking.Api.csproj src/Services/Booking/Booking.Api/
RUN dotnet restore src/Services/Booking/Booking.Api/Booking.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/Services/Booking/ src/Services/Booking/
RUN dotnet publish src/Services/Booking/Booking.Api/Booking.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Booking.Api.dll"]
