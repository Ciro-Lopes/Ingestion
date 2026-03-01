FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first to leverage Docker layer cache for restore
COPY ingestion.sln .
COPY src/Ingestion.Domain/Ingestion.Domain.csproj src/Ingestion.Domain/
COPY src/Ingestion.Application/Ingestion.Application.csproj src/Ingestion.Application/
COPY src/Ingestion.Infrastructure/Ingestion.Infrastructure.csproj src/Ingestion.Infrastructure/
COPY src/Ingestion.Worker/Ingestion.Worker.csproj src/Ingestion.Worker/
RUN dotnet restore src/Ingestion.Worker/Ingestion.Worker.csproj

# Copy source and publish
COPY . .
RUN dotnet publish src/Ingestion.Worker/Ingestion.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Ingestion.Worker.dll"]
