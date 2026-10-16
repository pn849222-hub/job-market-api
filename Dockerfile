# Build and publish, then copy only the output into a runtime-only image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project file alone, so a code-only change reuses this layer.
COPY src/JobMarketApi/JobMarketApi.csproj src/JobMarketApi/
RUN dotnet restore src/JobMarketApi/JobMarketApi.csproj

COPY src/ src/
RUN dotnet publish src/JobMarketApi/JobMarketApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as a non-root user; the image never needs to write to its own filesystem.
RUN useradd --uid 1001 --create-home --shell /usr/sbin/nologin api
USER api

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    Database__Path=/data/jobs.db
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["dotnet", "JobMarketApi.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "JobMarketApi.dll"]
