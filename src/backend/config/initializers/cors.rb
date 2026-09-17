# requirements.md 28章「配信元として想定する配信元のみを受け入れる」に対応。
# 実際の拒否判定は Api::BaseController#enforce_allowed_origin（サーバー側の403応答）が担う。
# ここではブラウザから見た正しいCORSヘッダ（Access-Control-Allow-Origin等）を、
# 許可リストに含まれるOriginに対してのみ付与する。
allowed_origins = ENV.fetch("ALLOWED_ORIGINS", "").split(",").map(&:strip).reject(&:empty?)

Rails.application.config.middleware.insert_before 0, Rack::Cors do
  allow do
    origins(*allowed_origins)

    resource "/api/v1/*",
      headers: :any,
      methods: [:get, :post, :patch, :options],
      credentials: false
  end
end
