# broker (API). Contexto de build = esta pasta, e nao src/:
# e aqui que vivem Directory.Build.props, Directory.Packages.props e global.json.
# Sem eles o csproj nao tem TargetFramework nem versao de pacote, e o restore falha.
#
# Runtime (aspnet) e SDK versionam diferente: nao existe tag aspnet:10.0.402.
# O global.json pede 10.0.204 com rollForward latestFeature. Um SDK 10.0 mais
# novo, como 10.0.402, satisfaz esse pedido.
ARG DOTNET_VERSION=10.0
ARG DOTNET_SDK_VERSION=10.0.402

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/broker.Api/broker.Api.csproj                             src/broker.Api/
COPY src/broker.Application/broker.Application.csproj             src/broker.Application/
COPY src/broker.Infrastructure/broker.Infrastructure.csproj       src/broker.Infrastructure/
COPY src/broker.Domain/broker.Domain.csproj                       src/broker.Domain/
RUN dotnet restore src/broker.Api/broker.Api.csproj
COPY . .
# Declarado DEPOIS do COPY . . de proposito: essa linha ja invalida o cache a cada
# mudanca de fonte, entao o ARG aqui nao custa o restore inteiro a cada commit.
ARG GIT_SHA=desconhecido
# /p:SourceRevisionId assa o commit no AssemblyInformationalVersion (1.0.0+<sha>).
RUN dotnet publish src/broker.Api/broker.Api.csproj -c Release -o /app /p:UseAppHost=false /p:SourceRevisionId=$GIT_SHA

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
# ARG nao atravessa estagio: precisa ser redeclarado para o LABEL e o /app/COMMIT.
ARG GIT_SHA=desconhecido
ARG GIT_BRANCH=desconhecido
LABEL org.opencontainers.image.title="broker" \
      org.opencontainers.image.revision="$GIT_SHA" \
      org.opencontainers.image.version="$GIT_BRANCH"
# libssl/ca-certificates para SQL Server (TLS) e chamadas HTTPS. curl para o HEALTHCHECK.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates libssl3 curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./
# O marcador que se le com `docker exec ... cat /app/COMMIT`.
# Vem DEPOIS do COPY --from=build, senao o COPY apaga o arquivo.
RUN echo "$GIT_SHA" > /app/COMMIT

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=45s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "broker.Api.dll"]
