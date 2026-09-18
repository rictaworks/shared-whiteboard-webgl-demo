# ルーティングはINTEGRATION_CONTRACT.md 第2章・第3章のパス・パラメータ名に厳密に一致させる。
Rails.application.routes.draw do
  namespace :api do
    namespace :v1 do
      post "sessions", to: "sessions#create"

      post "boards", to: "boards#create"
      get "boards", to: "boards#index"
      patch "boards/:board_id", to: "boards#update"
      get "boards/by_token/:board_token", to: "boards#show_by_token"
      post "boards/by_token/:board_token/join", to: "boards#join"
      get "boards/:board_id/ops", to: "boards#ops"
    end
  end

  namespace :internal do
    post "verify_participation", to: "verify_participation#create"
    post "boards/:board_id/ops", to: "boards#create_ops"
    get "boards/:board_id/last_seq", to: "boards#last_seq"
    post "boards/:board_id/undo_flag", to: "boards#undo_flag"

    # Issue #11：Go中継サーバーの契約(INTEGRATION_CONTRACT.md)には含まれない。
    # Railway Cronサービスから日次リセットをトリガーするための専用エンドポイント。
    post "system/daily_reset", to: "system#daily_reset"
  end
end
