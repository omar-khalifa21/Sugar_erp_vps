# syntax=docker/dockerfile:1.7
FROM node:22.19.0-alpine AS dependencies
WORKDIR /workspace
RUN npm install --global npm@11.3.0
COPY package.json package-lock.json ./
COPY apps/server/package.json apps/server/package.json
COPY apps/admin-web/package.json apps/admin-web/package.json
COPY packages/contracts/package.json packages/contracts/package.json
RUN npm ci

FROM dependencies AS development
COPY . .
RUN npm run prisma:generate
CMD ["npm", "run", "start:dev", "--workspace", "@sugar-erp/server"]

FROM dependencies AS build
COPY . .
RUN npm run prisma:generate && npm run build

FROM nginx:1.29.1-alpine AS web
COPY --from=build /workspace/apps/admin-web/dist /usr/share/nginx/html

FROM node:22.19.0-alpine AS production-dependencies
WORKDIR /workspace
ENV NODE_ENV=production
RUN npm install --global npm@11.3.0
COPY package.json package-lock.json ./
COPY apps/server/package.json apps/server/package.json
RUN npm ci --omit=dev && npm cache clean --force

FROM node:22.19.0-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
COPY --from=production-dependencies --chown=node:node /workspace/node_modules ./node_modules
COPY --from=build --chown=node:node /workspace/node_modules/.prisma ./node_modules/.prisma
COPY --from=build --chown=node:node /workspace/apps/server/dist ./dist
USER node
EXPOSE 3000
CMD ["node", "dist/src/main.js"]
