# Issue #11：日次リセット(JST 03:00)をRailway Cronサービスからhttps経由でトリガーするための内部エンドポイント。
# SQLiteのVolumeは1サービスにしかマウントできないため、リセット専用サービスを分けて直接DBを叩くことができない。
# 代わりに、DBを持つbackend自身にこのエンドポイントを置き、Volume不要の軽量なcronサービスから叩く構成にする。
module Internal
  class SystemController < Internal::BaseController
    # POST /internal/system/daily_reset
    def daily_reset
      DailyResetJob.new.run
      render json: { ok: true }, status: :ok
    end
  end
end
