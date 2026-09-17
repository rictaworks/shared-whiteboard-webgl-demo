# INTEGRATION_CONTRACT.md 2章：POST /api/v1/sessions。ヘッダ不要・ボディ不要。
module Api
  module V1
    class SessionsController < Api::BaseController
      def create
        session = Session.create!
        render json: { session_key: session.id }, status: :created
      end
    end
  end
end
