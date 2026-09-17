Rails.application.configure do
  config.enable_reloading = false
  config.eager_load = true
  config.consider_all_requests_local = false
  config.log_level = :info

  # RailwayはコンテナのSTDOUT/STDERRのみをログとして収集するため、
  # デフォルトのファイル出力（log/production.log）のままでは例外の
  # スタックトレースを本番で確認できない。
  if ENV["RAILS_LOG_TO_STDOUT"].present?
    logger = ActiveSupport::Logger.new($stdout)
    logger.formatter = config.log_formatter
    config.logger = ActiveSupport::TaggedLogging.new(logger)
  end
end
