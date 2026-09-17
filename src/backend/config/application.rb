require_relative "boot"

require "rails"
# 必要なフレームワークのみ読み込む（YAGNI）。
# ビュー・アセットパイプライン・ActionMailer・ActiveStorage・ActionText・
# ActionMailbox・ActionCableは本アプリでは使用しない（中継サーバーがGoで別に動くため）。
require "active_model/railtie"
require "active_record/railtie"
require "action_controller/railtie"

Bundler.require(*Rails.groups)

module SharedWhiteboardBackend
  class Application < Rails::Application
    config.load_defaults 7.2

    # APIモード：ビュー・Cookie/セッション・flashミドルウェア等を無効化する
    config.api_only = true

    # 表示・ログはJST固定（日本語版のみ開発。requirements.md CLAUDE.md準拠）
    config.time_zone = "Tokyo"
  end
end
