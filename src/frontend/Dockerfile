# This stage's Node binary also runs the render server in the final image.
FROM node:22.23.3-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402 AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# One image, two services: "frontend" runs nginx and "ssr" runs the render server with Node.
FROM nginx:1.30.6-alpine@sha256:970364228b1e4f932abc22d321932816782cca8a20b85a77552a7cfe9acfb3b5 AS final

# With a pinned base this layer stays cached forever; bump the date to pick up security fixes.
ARG APK_UPGRADE_DATE=2026-10-04
RUN apk upgrade --no-cache && apk add --no-cache libstdc++

COPY --from=build /usr/local/bin/node /usr/local/bin/node

RUN rm -rf /usr/share/nginx/html/*

# The server bundle is self-contained and needs no node_modules
COPY --from=build /app/dist/Frontend /app

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-security-headers.conf /etc/nginx/snippets/security-headers.conf

RUN sed -i -e '/^user /d' -e 's,^pid .*,pid /tmp/nginx.pid;,' /etc/nginx/nginx.conf \
    && grep -q '^pid /tmp/nginx.pid;' /etc/nginx/nginx.conf \
    && ! grep -q '^user ' /etc/nginx/nginx.conf \
    && chown -R nginx:nginx /var/cache/nginx
USER nginx

# deploy/deploy.sh reads this to expect server-rendered pages
LABEL dev.pkgdiff.rendering="server"

EXPOSE 80 4000
ENTRYPOINT ["nginx", "-g", "daemon off;"]
