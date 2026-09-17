Rails.application.configure do
  config.enable_reloading = false
  config.eager_load = ENV["CI"].present?
  config.consider_all_requests_local = true
  config.action_dispatch.show_exceptions = :none

  config.active_support.deprecation = :stderr
  config.action_controller.raise_on_missing_callback_actions = true

  config.log_level = :warn
end
