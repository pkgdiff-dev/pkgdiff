# Build stage: the SDK image that generated the lock files (same reference as in Directory.Build.props)
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build

# Emergency only: --build-arg PKGDIFF_AUDIT_AS_ERRORS=false turns NU1901-NU1904 back into warnings
ARG PKGDIFF_AUDIT_AS_ERRORS=true
WORKDIR /src

# Central versions, audit settings, the package source and the lock files come first,
# so the restore layer stays cached until a dependency changes
COPY ["PkgDiff.slnx", "Directory.Packages.props", "Directory.Build.props", "nuget.config", "./"]
COPY ["PkgDiff.API/PkgDiff.csproj", "PkgDiff.API/packages.lock.json", "PkgDiff.API/"]
COPY ["PkgDiff.Application/PkgDiff.Application.csproj", "PkgDiff.Application/packages.lock.json", "PkgDiff.Application/"]
COPY ["PkgDiff.Domain/PkgDiff.Domain.csproj", "PkgDiff.Domain/packages.lock.json", "PkgDiff.Domain/"]
COPY ["PkgDiff.Infrastructure/PkgDiff.Infrastructure.csproj", "PkgDiff.Infrastructure/packages.lock.json", "PkgDiff.Infrastructure/"]
COPY ["PkgDiff.Shared/PkgDiff.Shared.csproj", "PkgDiff.Shared/packages.lock.json", "PkgDiff.Shared/"]

# Restores exactly what the lock files record. NuGet audit runs inside this restore, so a
# known-vulnerable package fails the build here, but only when this layer is rebuilt (cold cache
# or a changed manifest). .github/scripts/check-deps.sh is the check that audits on every run.
RUN dotnet restore "PkgDiff.API/PkgDiff.csproj" --locked-mode -p:ContinuousIntegrationBuild=true -p:PkgDiffAuditAsErrors=${PKGDIFF_AUDIT_AS_ERRORS}

COPY . .

# Publish reuses the restore above, so nothing is resolved a second time
WORKDIR /src/PkgDiff.API
RUN dotnet publish "PkgDiff.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false -p:PkgDiffAuditAsErrors=${PKGDIFF_AUDIT_AS_ERRORS}

# Runtime stage: ASP.NET Core 10.0.13 on Ubuntu 24.04, pinned by tag and index digest
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS final
WORKDIR /app
EXPOSE 8080

# Pick up Ubuntu security fixes published after the runtime image was built, and install curl,
# which the compose healthcheck calls.
# The base digest is pinned, so Docker would reuse this layer from its build cache
# forever; bump the date (or pass --build-arg APT_UPGRADE_DATE=...) to run it again.
ARG APT_UPGRADE_DATE=2026-10-03
# Non-interactive, and a changed configuration file keeps the installed version: an upgrade must
# never stop the image build at a question. The variable is set for this layer only, not as ENV.
RUN export DEBIAN_FRONTEND=noninteractive \
    && apt-get update \
    && apt-get upgrade -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The unprivileged user the ASP.NET image ships (uid 1654). The application writes nothing next
# to its files; a settings file mounted by compose has to be readable for this user
# (deploy/deploy.sh checks that before it switches).
USER $APP_UID
ENTRYPOINT ["dotnet", "PkgDiff.dll"]
