# シンプルで安全なマルチステージ Dockerfile (.NET 8)
# - ビルドは SDK イメージで行い、公開成果物のみを最小ランタイムイメージへコピーします
# - 非 root ユーザーで実行するように設定しています

# ==========================================
# ビルドステージ
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# プロジェクトファイルを先にコピーして restore
# （Dockerキャッシュを有効にするため）
COPY ["TacoDiscordBot/TacoDiscordBot.csproj", "TacoDiscordBot/"]

RUN dotnet restore "TacoDiscordBot/TacoDiscordBot.csproj"

# 全ソースをコピーして publish
COPY . .

WORKDIR /src/TacoDiscordBot

RUN dotnet publish "TacoDiscordBot.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore


# ==========================================
# 実行ステージ
# ==========================================
FROM mcr.microsoft.com/dotnet/runtime:8.0

# PostgreSQL / GSSAPI(Kerberos) 関連ライブラリ
# libgssapi_krb5.so.2 を提供する
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# ==========================================
# 非 root ユーザー作成
# ==========================================
RUN set -eux; \
    if ! getent group app >/dev/null 2>&1; then \
        groupadd -r app; \
    fi; \
    if ! id -u app >/dev/null 2>&1; then \
        useradd -r -g app -s /sbin/nologin -M app; \
    fi

WORKDIR /app

# 公開成果物をコピー
COPY --from=build /app/publish .

# 非 root ユーザーで実行
USER app

# コンテナ内で実行していることを.NETに通知
ENV DOTNET_RUNNING_IN_CONTAINER=true

# 起動
ENTRYPOINT ["dotnet", "TacoDiscordBot.dll"]