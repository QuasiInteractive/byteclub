# 1. the React booking page and dashboard
FROM node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# the build writes to ../api/wwwroot (see vite.config.ts)
RUN npm run build

# 2. the .NET API, with the built site in wwwroot
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY api/ByteClub.Api.csproj api/
RUN dotnet restore api/ByteClub.Api.csproj
COPY api/ api/
COPY --from=web /src/api/wwwroot api/wwwroot
RUN dotnet publish api/ByteClub.Api.csproj -c Release -o /out --no-restore

# 3. runtime only
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ByteClub.Api.dll"]
