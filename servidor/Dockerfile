FROM node:22-alpine
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY servidor.js ./
ENV PORT=8080 TRUST_PROXY=1
EXPOSE 8080
USER node
CMD ["node", "servidor.js"]
