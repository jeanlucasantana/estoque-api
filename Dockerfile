# syntax=docker/dockerfile:1

# ---------- build: restaura em camada própria e publica ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primeiro, só os arquivos que influenciam o restore. Enquanto eles não mudarem,
# a camada de restore vem do cache e uma alteração de código não baixa os pacotes de novo.
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json ./
COPY src/Estoque.Api/Estoque.Api.csproj src/Estoque.Api/
RUN dotnet restore src/Estoque.Api/Estoque.Api.csproj -r linux-x64 \
    && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/Estoque.Api/Estoque.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish

# ---------- bundle: executável que aplica as migrações do EF Core ----------
FROM build AS bundle
# Sem --self-contained: o bundle usa o runtime do .NET que já existe na imagem de destino.
# Atenção: no dotnet ef, "-c" é --context; a configuração de build precisa ser --configuration.
RUN dotnet ef migrations bundle --project src/Estoque.Api/Estoque.Api.csproj --configuration Release -r linux-x64 -o /app/efbundle --force

# ---------- migrations: roda uma vez no Compose, antes da API ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS migrations
WORKDIR /app
COPY --from=bundle /app/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# ---------- final: só o runtime, sem SDK, sem shell e sem root ----------
# "extra" inclui ICU e tzdata: sem o tzdata, o fuso America/Sao_Paulo da promoção de sexta não existe na imagem.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Estoque.Api.dll"]
