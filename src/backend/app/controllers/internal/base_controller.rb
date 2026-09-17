# requirements.md 3章：内部APIはGo中継サーバーのみが呼ぶ。X-Internal-Secretヘッダで検証する。
module Internal
  class BaseController < ApplicationController
    before_action :require_internal_secret

    private

    def require_internal_secret
      provided = request.headers["X-Internal-Secret"]
      expected = ENV["INTERNAL_SHARED_SECRET"]

      if expected.blank? || provided.blank? || !ActiveSupport::SecurityUtils.secure_compare(provided.to_s, expected)
        render json: { error: "unauthorized" }, status: :unauthorized
      end
    end
  end
end
