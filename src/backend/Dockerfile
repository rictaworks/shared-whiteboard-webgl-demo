FROM ruby:3.3-slim AS build

RUN apt-get update -qq && apt-get install -y --no-install-recommends \
    build-essential libsqlite3-dev git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /rails

COPY Gemfile Gemfile.lock ./
RUN bundle config set --local without 'development test' && bundle install

COPY . .

# Windowsクローンからのビルドではシバン行の実行ビット・改行がDockerイメージへ
# そのまま持ち込めないため、ここで明示的に付与・変換する
# (20_開発/.claude/agents/railway-deploy-ops.md 既知の落とし穴)。
RUN chmod +x bin/* && sed -i 's/\r$//' bin/*

FROM ruby:3.3-slim

RUN apt-get update -qq && apt-get install -y --no-install-recommends \
    libsqlite3-0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /rails
COPY --from=build /usr/local/bundle /usr/local/bundle
COPY --from=build /rails /rails

# 永続化ボリュームのマウント先（database.yml参照）。db/ ではなく専用ディレクトリに
# することで、アプリケーションコード（db/migrate/等）がボリュームで隠れないようにする。
RUN mkdir -p storage

ENV RAILS_ENV=production
ENV RAILS_LOG_TO_STDOUT=true

EXPOSE 3000

# 起動コマンドを明示指定するとイメージのENTRYPOINTは実行されないため、
# db:prepare（マイグレーション適用）を起動コマンド自体に含める。失敗したら
# サーバーを起動しない（20_開発/.claude/agents/railway-deploy-ops.md 既知の落とし穴）。
# Railwayが動的に注入する $PORT を必ず listen する（固定ポートだとhealthcheckが
# 静かに落ち続ける、という同ファイルの別の落とし穴も併せて回避する）。
CMD ["/bin/sh", "-c", "set -e; echo 'db:prepare...'; bin/rails db:prepare; echo 'starting server...'; exec bin/rails server -b 0.0.0.0 -p ${PORT:-3000} -e production"]
