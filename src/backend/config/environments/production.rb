# 本タスクの範囲外（Railwayへの本番デプロイは別工程）。将来のデプロイに備えた最小限の設定のみ用意する。
Rails.application.configure do
  config.enable_reloading = false
  config.eager_load = true
  config.consider_all_requests_local = false
  config.log_level = :info
end
