# INTEGRATION_CONTRACT.md 3章：POST /internal/verify_participation
# HTTPステータスは200固定で、reasonフィールドで判別する（中継側の分岐を単純化するための契約）。
module Internal
  class VerifyParticipationController < Internal::BaseController
    def create
      board = Board.find_by(board_token: params[:board_token])
      return render json: { ok: false, reason: "not_found" }, status: :ok if board.nil?

      participation = Participation.find_by(session_id: params[:session_key], board_id: board.id)
      return render json: { ok: false, reason: "verification_failed" }, status: :ok if participation.nil?

      render json: {
        ok: true,
        board_id: board.id,
        label: participation.label,
        color: participation.color,
        participation_id: participation.id
      }, status: :ok
    end
  end
end
