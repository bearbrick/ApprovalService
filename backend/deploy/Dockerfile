# 从仓库根目录构建：docker build -f backend/deploy/Dockerfile -t approval-service .
FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src/backend
COPY backend/ ./
RUN dotnet publish src/ApprovalService.Api/ApprovalService.Api.csproj -c Release -r linux-x64 --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend-build /app ./
COPY --from=frontend-build /src/frontend/dist ./wwwroot/
RUN mkdir -p /data && chown app:app /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ApprovalService__Data__Directory=/data
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ApprovalService.Api.dll"]
