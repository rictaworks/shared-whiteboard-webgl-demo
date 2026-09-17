ENV["BUNDLE_GEMFILE"] ||= File.expand_path("../Gemfile", __dir__)

require "bundler/setup"

# ルートの .env を読み込む（Go中継サーバーと共有。INTEGRATION_CONTRACT.md 0章:
# 「環境変数は .env（ルートに1つ）を両サーバーが読む」）。
# dotenv系gemを増やさず、シンプルな行パーサーで足りる（YAGNI）。既存のENVは上書きしない。
root_env_path = File.expand_path("../../../.env", __dir__)
if File.exist?(root_env_path)
  File.foreach(root_env_path) do |line|
    line = line.strip
    next if line.empty? || line.start_with?("#")

    key, value = line.split("=", 2)
    next if key.nil? || value.nil?

    ENV[key.strip] ||= value.strip.gsub(/\A["']|["']\z/, "")
  end
end
