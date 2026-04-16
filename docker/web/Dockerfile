# DigiByte Web — Multi-stage .NET 10 Blazor WASM + Nginx

# === Build stage ===
# Use node:20-alpine as the base so Node.js/npm are pre-installed,
# avoiding apt-get network calls that can fail in restricted build environments.
FROM node:20-alpine AS build
WORKDIR /src

# Install .NET SDK 10 runtime dependencies, then use the official install script
RUN apk add --no-cache bash icu-libs krb5-libs libgcc libintl libssl3 libstdc++ zlib wget \
    && wget -qO /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/dotnet \
    && ln -s /usr/local/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh

# Copy solution and project files first (for layer caching)
COPY DigiByte.slnx .
COPY src/DigiByte.Api/DigiByte.Api.csproj src/DigiByte.Api/
COPY src/DigiByte.Wallet/DigiByte.Wallet.csproj src/DigiByte.Wallet/
COPY src/DigiByte.Crypto/DigiByte.Crypto.csproj src/DigiByte.Crypto/
COPY src/DigiByte.P2P.Shared/DigiByte.P2P.Shared.csproj src/DigiByte.P2P.Shared/
COPY src/DigiByte.NodeApi/DigiByte.NodeApi.csproj src/DigiByte.NodeApi/
COPY src/DigiByte.Web/DigiByte.Web.csproj src/DigiByte.Web/
COPY tests/DigiByte.Crypto.Tests/DigiByte.Crypto.Tests.csproj tests/DigiByte.Crypto.Tests/
COPY tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj tests/DigiByte.Wallet.Tests/
COPY tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj tests/DigiByte.Api.Tests/
COPY tests/DigiByte.NodeApi.Tests/DigiByte.NodeApi.Tests.csproj tests/DigiByte.NodeApi.Tests/

# Restore Web and its dependencies
RUN dotnet restore src/DigiByte.Web/DigiByte.Web.csproj

# Copy everything
COPY . .

# Install npm dependencies for Tailwind CSS
WORKDIR /src/src/DigiByte.Web
RUN npm install

# Publish Blazor WASM (includes Tailwind build via MSBuild target)
WORKDIR /src
RUN dotnet publish src/DigiByte.Web/DigiByte.Web.csproj \
    -c Release -o /app/publish --no-restore

# Copy nginx config into a known location for the runtime stage
RUN cp docker/web/nginx.conf /app/publish/nginx.conf

# === Runtime stage — Nginx to serve static files ===
FROM nginx:alpine AS runtime

# Remove default nginx site
RUN rm -rf /usr/share/nginx/html/*

# Copy published Blazor WASM output and nginx config from build stage
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY --from=build /app/publish/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
