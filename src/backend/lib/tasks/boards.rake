namespace :boards do
  desc "日次リセット：中継サーバーへ全ハブ破棄を要求し、全テーブルを削除する（JST 03:00に実行する想定）"
  task daily_reset: :environment do
    DailyResetJob.new.run
  end
end
