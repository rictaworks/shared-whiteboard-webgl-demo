# requirements.md 29章・23.7節：JST 03:00の日次リセット。
# ActiveJobは使わない（キュー・非同期実行が不要なYAGNI判断。rake taskから同期的にrunを呼ぶだけで足りる）。
require "net/http"
require "uri"

class DailyResetJob
  def run
    request_hub_shutdown
    purge_all
  end

  # 中継サーバーへ全ハブ破棄・致命通知を要求する（内部）。
  # 中継サーバーが無料枠で停止している等で失敗しても、データ削除（purge_all）は必ず実行する
  # （日次リセットの主目的はデータ削除であり、中継への通知はベストエフォート）。
  def request_hub_shutdown
    relay_url = ENV["RELAY_INTERNAL_URL"]
    return if relay_url.blank?

    uri = URI.join(relay_url, "/internal/reset")
    request = Net::HTTP::Post.new(uri)
    request["X-Internal-Secret"] = ENV["INTERNAL_SHARED_SECRET"]

    Net::HTTP.start(uri.host, uri.port, use_ssl: uri.scheme == "https") do |http|
      http.request(request)
    end
  rescue StandardError => e
    Rails.logger.error("DailyResetJob: relay reset request failed: #{e.class}")
  end

  def purge_all
    ActiveRecord::Base.transaction do
      BoardOp.delete_all
      Stroke.delete_all
      Participation.delete_all
      Board.delete_all
      Session.delete_all
    end
  end
end
