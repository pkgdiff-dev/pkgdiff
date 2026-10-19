# The SDK that generated the lock files; must match the reference in Directory.Build.props
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build

# Emergency only: --build-arg PKGDIFF_AUDIT_AS_ERRORS=false turns NU1901-NU1904 back into warnings
ARG PKGDIFF_AUDIT_AS_ERRORS=true
WORKDIR /src

COPY ["PkgDiff.slnx", "Directory.Packages.props", "Directory.Build.props", "nuget.config", "./"]
COPY ["PkgDiff.API/PkgDiff.csproj", "PkgDiff.API/packages.lock.json", "PkgDiff.API/"]
COPY ["PkgDiff.Application/PkgDiff.Application.csproj", "PkgDiff.Application/packages.lock.json", "PkgDiff.Application/"]
COPY ["PkgDiff.Domain/PkgDiff.Domain.csproj", "PkgDiff.Domain/packages.lock.json", "PkgDiff.Domain/"]
COPY ["PkgDiff.Infrastructure/PkgDiff.Infrastructure.csproj", "PkgDiff.Infrastructure/packages.lock.json", "PkgDiff.Infrastructure/"]
COPY ["PkgDiff.Shared/PkgDiff.Shared.csproj", "PkgDiff.Shared/packages.lock.json", "PkgDiff.Shared/"]

# The audit here only runs when this layer rebuilds; check-deps.sh audits on every run.
RUN dotnet restore "PkgDiff.API/PkgDiff.csproj" --locked-mode -p:ContinuousIntegrationBuild=true -p:PkgDiffAuditAsErrors=${PKGDIFF_AUDIT_AS_ERRORS}

COPY . .

WORKDIR /src/PkgDiff.API
RUN dotnet publish "PkgDiff.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false -p:PkgDiffAuditAsErrors=${PKGDIFF_AUDIT_AS_ERRORS}

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final
WORKDIR /app
EXPOSE 8080

# With a pinned base this layer stays cached forever; bump the date to pick up security fixes.
# curl is needed by the compose healthcheck.
ARG APT_UPGRADE_DATE=2026-10-03
RUN export DEBIAN_FRONTEND=noninteractive \
    && apt-get update \
    && apt-get upgrade -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "PkgDiff.dll"]
