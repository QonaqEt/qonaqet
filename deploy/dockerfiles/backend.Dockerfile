# syntax=docker/dockerfile:1
# Backend host-larının ortaq Dockerfile-ı (E01-T17). Bir host seçilir:
#
#   docker build -f deploy/dockerfiles/backend.Dockerfile --build-arg PROJECT=Qonaqet.Api .
#
# PROJECT: Qonaqet.Api | Qonaqet.Integration.Api | Qonaqet.Worker
# Image-lər konkret versiya ilə pinlənib (latest yoxdur). Runtime image chiseled-dir: shell yoxdur,
# default istifadəçi non-root `app`-dır. Worker də aspnet runtime-ı işlədir, çünki paylaşılan
# Infrastructure layihəsi Microsoft.AspNetCore.App framework-una istinad edir.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.404-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled

FROM ${SDK_IMAGE} AS build
ARG PROJECT
RUN test -n "${PROJECT}" || (echo "PROJECT build arqumenti lazımdır" >&2 && exit 1)
ENV DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true CI=true
WORKDIR /src/backend

# Əvvəl yalnız restore üçün lazım olan fayllar: kod dəyişəndə paket qatı keşdən gəlir.
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props ./
COPY backend/src/BuildingBlocks/Qonaqet.SharedKernel/*.csproj backend/src/BuildingBlocks/Qonaqet.SharedKernel/packages.lock.json src/BuildingBlocks/Qonaqet.SharedKernel/
COPY backend/src/Qonaqet.Domain/*.csproj backend/src/Qonaqet.Domain/packages.lock.json src/Qonaqet.Domain/
COPY backend/src/Qonaqet.Contracts/*.csproj backend/src/Qonaqet.Contracts/packages.lock.json src/Qonaqet.Contracts/
COPY backend/src/Qonaqet.Application/*.csproj backend/src/Qonaqet.Application/packages.lock.json src/Qonaqet.Application/
COPY backend/src/Qonaqet.Infrastructure/*.csproj backend/src/Qonaqet.Infrastructure/packages.lock.json src/Qonaqet.Infrastructure/
COPY backend/src/${PROJECT}/*.csproj backend/src/${PROJECT}/packages.lock.json src/${PROJECT}/
RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj" --locked-mode

# Email şablonlarının rəngləri design-tokens/tokens.json-dan embedded olunur (Infrastructure csproj).
COPY design-tokens/tokens.json ../design-tokens/tokens.json
COPY backend/src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release --no-restore -o /app \
    && mv "/app/${PROJECT}" /app/host

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /app ./
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
# Chiseled image-də curl/shell yoxdur: host özü probe rejimində sağlamlığı yoxlayır.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 CMD ["/app/host", "--health-probe"]
ENTRYPOINT ["/app/host"]
