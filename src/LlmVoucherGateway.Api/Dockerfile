# Build context is the repository root, for the shared build settings and the Domain project:
#   docker build -f src/LlmVoucherGateway.Api/Dockerfile -t llm-voucher-gateway-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so the restore layer is reused until a dependency changes.
COPY Directory.Build.props .editorconfig ./
COPY src/LlmVoucherGateway.Domain/LlmVoucherGateway.Domain.csproj src/LlmVoucherGateway.Domain/
COPY src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.csproj src/LlmVoucherGateway.Api/
RUN dotnet restore src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.csproj

COPY src/LlmVoucherGateway.Domain/ src/LlmVoucherGateway.Domain/
COPY src/LlmVoucherGateway.Api/ src/LlmVoucherGateway.Api/
RUN dotnet publish src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# Runs as the image's non-root user, UID 1654. A bind mount at /data must be writable by it;
# a new named volume copies the ownership of the directory created here.
RUN mkdir /data && chown $APP_UID:$APP_UID /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "LlmVoucherGateway.Api.dll"]
