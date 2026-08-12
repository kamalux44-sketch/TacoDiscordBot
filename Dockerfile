# シンプルで安全なマルチステージ Dockerfile (.NET 8)
# - ビルドは SDK イメージで行い、公開成果物のみを最小ランタイムイメージへコピーします
# - 非 root ユーザーで実行するように設定しています

# ビルドステージ
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# プロジェクトファイルを先にコピーして restore（キャッシュを有効にするため）
COPY ["TacoDiscordBot/TacoDiscordBot.csproj", "TacoDiscordBot/"]
RUN dotnet restore "TacoDiscordBot/TacoDiscordBot.csproj"

# 全ソースをコピーして publish
COPY . .
WORKDIR /src/TacoDiscordBot
RUN dotnet publish "TacoDiscordBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore

# 実行ステージ（ランタイムのみ）
FROM mcr.microsoft.com/dotnet/runtime:8.0

# セキュリティのため、非 root ユーザーを作成して実行
# 既に存在する場合もエラーにしないように条件付きで作成する
RUN set -eux; \
	if ! getent group app >/dev/null 2>&1; then groupadd -r app; fi; \
	if ! id -u app >/dev/null 2>&1; then useradd -r -g app -s /sbin/nologin -M app; fi
WORKDIR /app

# 公開成果物をコピー
COPY --from=build /app/publish .

# 実行ユーザーを切り替え
USER app

# 環境変数（必要なら実行時に -e で上書きしてください）
ENV DOTNET_RUNNING_IN_CONTAINER=true

# コンテナ起動コマンド
ENTRYPOINT ["dotnet", "TacoDiscordBot.dll"]
