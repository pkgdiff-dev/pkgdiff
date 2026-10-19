# Build stage: Node 22 LTS, pinned by tag and index digest. The Node binary of this stage also runs
# the render server in the final image, so what is built and tested is what runs.
FROM node:22.23.3-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402 AS build
WORKDIR /app

# The lock file is mandatory: the install below takes exactly what it records
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# Runtime stage: nginx stable line, pinned by tag and index digest.
# One image, two compose services: "frontend" runs nginx (the entrypoint below) and "ssr" runs the
# render server with Node, so the files nginx serves and the pages that refer to them are one build.
# Neither runs as root.
FROM nginx:1.30.6-alpine@sha256:970364228b1e4f932abc22d321932816782cca8a20b85a77552a7cfe9acfb3b5 AS final

# Pick up Alpine security fixes published after the nginx image was built, and install the C++
# runtime the Node binary links against.
# The base digest is pinned, so Docker would reuse this layer from its build cache
# forever; bump the date (or pass --build-arg APK_UPGRADE_DATE=...) to run it again.
ARG APK_UPGRADE_DATE=2026-10-04
RUN apk upgrade --no-cache && apk add --no-cache libstdc++

COPY --from=build /usr/local/bin/node /usr/local/bin/node

# Clean default Nginx files
RUN rm -rf /usr/share/nginx/html/*

# dist/Frontend holds browser/ (the files nginx serves) and server/ (the render server); the
# server bundle is self-contained and needs no node_modules
COPY --from=build /app/dist/Frontend /app

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-security-headers.conf /etc/nginx/snippets/security-headers.conf

# nginx runs as the unprivileged "nginx" user from its first process on: the pid file moves to
# /tmp, the "user" directive (only a root master can honour it) goes, and the directory of its
# temporary files becomes its own. Port 80 needs no privilege inside a container.
RUN sed -i -e '/^user /d' -e 's,^pid .*,pid /tmp/nginx.pid;,' /etc/nginx/nginx.conf \
    && grep -q '^pid /tmp/nginx.pid;' /etc/nginx/nginx.conf \
    && ! grep -q '^user ' /etc/nginx/nginx.conf \
    && chown -R nginx:nginx /var/cache/nginx
USER nginx

# The deploy script reads this to know that pages of this image must arrive rendered
LABEL dev.pkgdiff.rendering="server"

EXPOSE 80 4000
ENTRYPOINT ["nginx", "-g", "daemon off;"]
