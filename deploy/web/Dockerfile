# syntax=docker/dockerfile:1.7
FROM node:22.19.0-alpine AS build
WORKDIR /workspace
RUN npm install --global npm@11.3.0
COPY package.json package-lock.json ./
COPY apps/server/package.json apps/server/package.json
COPY apps/admin-web/package.json apps/admin-web/package.json
COPY packages/contracts/package.json packages/contracts/package.json
RUN npm ci
COPY apps/admin-web apps/admin-web
RUN npm run build --workspace @sugar-erp/admin-web

FROM nginx:1.29.1-alpine
COPY --from=build /workspace/apps/admin-web/dist /usr/share/nginx/html
