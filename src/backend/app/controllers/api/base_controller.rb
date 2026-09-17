# requirements.md 28章「中継・アプリケーション層は、配信元としてUnity Playの配信元のみを受け入れる」。
# Originヘッダが送られてきて、かつ許可リストに含まれない場合のみ拒否する
# （非ブラウザ・同一オリジンなどOriginヘッダが無い要求はここでは判定しない。CORS自体はrack-corsが別途処理する）。
module Api
  class BaseController < ApplicationController
    before_action :enforce_allowed_origin

    private

    def enforce_allowed_origin
      origin = request.headers["Origin"]
      return if origin.blank?
      return if allowed_origins.include?(origin)

      render json: { error: "origin_not_allowed" }, status: :forbidden
    end

    def allowed_origins
      ENV.fetch("ALLOWED_ORIGINS", "").split(",").map(&:strip).reject(&:empty?)
    end
  end
end
