# syntax=docker/dockerfile:1
# API image. Build context: backend/. Multi-arch base images, so it runs natively on Apple Silicon.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/EvidenceChain.Api/EvidenceChain.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EvidenceChain.Api.dll"]
