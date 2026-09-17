# requirements.md 6章「所有権と共有の原則」を実装するconcern。
# - X-Session-Key ヘッダの検証（無ければ401）
# - 「そのセッションの参加レコードがあるボード」に限定したアクセス制御（無ければ404。
#   403にはしない＝「ボードが存在しない」のか「参加していない」のかを外部に区別させない）
module OwnedByParticipation
  extend ActiveSupport::Concern

  included do
    before_action :require_session_key
  end

  private

  def require_session_key
    key = request.headers["X-Session-Key"]
    return render json: { error: "unauthorized" }, status: :unauthorized if key.blank?

    session = Session.find_by(id: key)
    return render json: { error: "unauthorized" }, status: :unauthorized if session.nil?

    session.touch_last_seen!
    @current_session = session
  end

  def current_session
    @current_session
  end

  # 参加レコードを検証する。無ければ404を書き込み、呼び出し元へ nil を返す。
  # 注意：render の戻り値は真偽値ではなく応答本文の文字列を返すため、
  # `return render(...) if 条件` のような書き方はガード節として機能しない（呼び出し元でnil判定できない）。
  # そのため render とは別に明示的に nil を return する。
  def participation_for!(board_id)
    participation = Participation.find_by(session_id: current_session.id, board_id: board_id)
    if participation.nil?
      render json: { error: "not_found" }, status: :not_found
      return nil
    end

    participation
  end
end
