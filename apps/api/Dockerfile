# Imagem das APIs. Uma imagem por host:
#   docker build -f apps/api/Dockerfile --build-arg HOST=Sinedu.Platform.Host -t sinedu/platform .
#   docker build -f apps/api/Dockerfile --build-arg HOST=Sinedu.Tenant.Host   -t sinedu/tenant .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG HOST
WORKDIR /src
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/src/${HOST} -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG HOST
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    SINEDU_HOST_DLL=${HOST}.dll
WORKDIR /app
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$SINEDU_HOST_DLL\" \"$@\"", "--"]
