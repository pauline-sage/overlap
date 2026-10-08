# Build the phone UI and API; only published output enters the runtime image.
FROM node:24-bookworm-slim AS mobile
WORKDIR /src/apps/mobile
COPY apps/mobile/package*.json ./
RUN npm ci
COPY apps/mobile/ ./
RUN npm run build -- --configuration production

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src
COPY NuGet.Config ./
COPY services/api/Overlap.Api.csproj services/api/
RUN dotnet restore services/api/Overlap.Api.csproj
COPY services/api/ services/api/
RUN dotnet publish services/api/Overlap.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS application
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
COPY --from=api /out/ ./
COPY --from=mobile /src/apps/mobile/dist/mobile/browser/ ./wwwroot/
USER app
ENTRYPOINT ["dotnet", "Overlap.Api.dll"]

# Optional one-off migration image. Supply the privileged migration connection
# privately at deployment time; normal application startup never migrates.
FROM application AS migrations
CMD ["--migrate"]

# Keep the default build target as the web application.
FROM application AS runtime
