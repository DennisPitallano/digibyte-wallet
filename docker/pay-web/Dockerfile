# DigiByte.Pay.Web — Blazor SSR merchant dashboard + hosted checkout.
# Multi-stage .NET 10 build. Unlike the main wallet's docker/web/Dockerfile
# (which static-hosts Blazor WASM behind Nginx), Pay.Web runs InteractiveServer
# — so the runtime is ASP.NET Core itself.

# === Build stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for cacheable restore. Pay.Web pulls DigiByte.Crypto
# (address validation, shared utilities) but not Wallet or Api.
COPY DigiByte.slnx .
COPY src/DigiByte.Pay.Web/DigiByte.Pay.Web.csproj src/DigiByte.Pay.Web/
COPY src/DigiByte.Crypto/DigiByte.Crypto.csproj src/DigiByte.Crypto/
COPY src/DigiByte.P2P.Shared/DigiByte.P2P.Shared.csproj src/DigiByte.P2P.Shared/
# Solution siblings — needed by restore to resolve references.
COPY src/DigiByte.Pay.Api/DigiByte.Pay.Api.csproj src/DigiByte.Pay.Api/
COPY src/DigiByte.Api/DigiByte.Api.csproj src/DigiByte.Api/
COPY src/DigiByte.Wallet/DigiByte.Wallet.csproj src/DigiByte.Wallet/
COPY src/DigiByte.NodeApi/DigiByte.NodeApi.csproj src/DigiByte.NodeApi/
COPY src/DigiByte.Web/DigiByte.Web.csproj src/DigiByte.Web/
COPY tests/DigiByte.Crypto.Tests/DigiByte.Crypto.Tests.csproj tests/DigiByte.Crypto.Tests/
COPY tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj tests/DigiByte.Wallet.Tests/
COPY tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj tests/DigiByte.Api.Tests/
COPY tests/DigiByte.NodeApi.Tests/DigiByte.NodeApi.Tests.csproj tests/DigiByte.NodeApi.Tests/

RUN dotnet restore src/DigiByte.Pay.Web/DigiByte.Pay.Web.csproj

# Full source. NB: the BuildTailwindCSS MSBuild target in Pay.Web.csproj is
# gated on `Exists('node_modules/.package-lock.json')`, so it's a no-op here —
# the Docker build uses the compiled wwwroot/css/tailwind.css that's already
# in the repo. Keeps the runtime image free of Node.
COPY . .
# Publish re-restores intentionally — Blazor Web App relies on package-
# contributed static web assets (_framework/blazor.web.js ships from
# Microsoft.AspNetCore.Components.Server) and the MSBuild targets that
# merge those into wwwroot/ don't always fire cleanly under --no-restore
# when the original restore ran in a separate Docker layer. Cost is one
# extra cached restore, pay-off is a working interactive render.
RUN dotnet publish src/DigiByte.Pay.Web/DigiByte.Pay.Web.csproj \
    -c Release -o /app/publish

# === Runtime stage ===
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Railway injects PORT; Program.cs binds to it. Fallback 8080 for local runs.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# Disable dev-only exception pages / HTTPS redirect paths.
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "DigiByte.Pay.Web.dll"]
