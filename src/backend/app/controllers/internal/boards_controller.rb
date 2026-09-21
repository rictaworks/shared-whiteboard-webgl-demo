# INTEGRATION_CONTRACT.md 3章：Go中継サーバーのみが呼ぶ内部エンドポイント。
# 連番の重複はスキップ（冪等）・欠番は409で再送を要求する（requirements.md 11.5節）。
module Internal
  class BoardsController < Internal::BaseController
    # requirements.md 17章「1セッションからの操作は毎秒30件まで」の二重防御（必須ではないが実装する）。
    RATE_LIMIT_PER_SECOND = 30

    # POST /internal/boards/:board_id/ops
    def create_ops
      board = Board.find_by(id: params[:board_id])
      return render_not_found if board.nil?

      incoming_ops = Array(params[:ops])
      return render json: { accepted: 0 }, status: :ok if incoming_ops.empty?

      sorted = incoming_ops.sort_by { |op| op[:seq].to_i }
      min_seq = sorted.first[:seq].to_i

      if min_seq > board.last_seq + 1
        return render json: { error: "gap_detected", expected_from: board.last_seq + 1 }, status: :conflict
      end

      accepted = 0
      op_limit_hit = false
      gap_detected = false

      ActiveRecord::Base.transaction do
        sorted.each do |raw_op|
          seq = raw_op[:seq].to_i
          next if seq <= board.last_seq # 連番の重複はスキップ（既に永続化済み＝冪等）

          # レビュー修正：requirements.md 11.5章「アプリケーション層は連番の重複を拒否し、
          # 欠番を検出した場合は該当範囲の再送を中継サーバーへ求めること」は、これまで
          # バッチ先頭（min_seq）でしか検証されておらず、バッチの中間に欠番があっても
          # そのまま連番を飛び越して書き込んでいた（例：last_seq=4のときseq=[5,6,8]が届くと、
          # 7が永久に欠落したままlast_seq=8として確定してしまう）。中間の欠番を検出した場合は
          # そこで処理を打ち切り、それまでの分だけ確定してgap_detectedを返す（中継サーバーは
          # 直近バッファから該当範囲を再送する）。
          if seq > board.last_seq + 1
            gap_detected = true
            break
          end

          if board.op_count >= Board::MAX_OPS
            op_limit_hit = true
            next
          end

          next unless rate_limit_ok?(raw_op[:session_key], board.id)

          persist_op!(board, raw_op)
          board.last_seq = seq
          board.op_count += 1
          accepted += 1
        end

        board.save!
      end

      return render json: { error: "gap_detected", expected_from: board.last_seq + 1 }, status: :conflict if gap_detected
      return render json: { error: "op_limit_exceeded" }, status: :conflict if op_limit_hit && accepted.zero?

      render json: { accepted: accepted }, status: :ok
    end

    # GET /internal/boards/:board_id/last_seq
    def last_seq
      board = Board.find_by(id: params[:board_id])
      return render_not_found if board.nil?

      render json: { last_seq: board.last_seq }, status: :ok
    end

    # POST /internal/boards/:board_id/undo_flag
    #
    # セキュリティレビュー修正（High）：requirements.md 12章「Undo / Redo は自分の操作のみを対象とし、
    # 他の参加者の操作には作用しないこと」。修正前はop_idの所有者チェックが無く、ボードの参加者であれば
    # WebSocket経由で任意の他人のop_id（stroke_add/stroke_erase/clear）に対するundo_flagを送るだけで
    # 他人の操作の取消状態を書き換えられた（中継サーバーは接続時にsession_key・board_tokenの組を
    # 照合するのみで、op単位の所有者までは検証しない前提のため、Railsが最終防衛線になる）。
    # INTEGRATION_CONTRACT.md 3章のボディに session_key が含まれているのはこの検証のためであり、
    # 本来使うべきだったフィールドが未使用のまま残っていた。
    def undo_flag
      board = Board.find_by(id: params[:board_id])
      return render_not_found if board.nil?

      op = BoardOp.find_by(op_id: params[:op_id], board_id: board.id)
      return render_not_found if op.nil?
      return render_not_owner unless op.session_id == params[:session_key].to_s

      op.update!(undone: ActiveModel::Type::Boolean.new.cast(params[:undone]))

      # 実害の修正（2026-09-21・Issue #38）：中継サーバーの undo_flag は
      # ops（stroke_add/stroke_erase/clear）と同じ連番（BoardHub.nextSeq）を
      # 1つ消費するが（hub.go の AssignUndoSeq）、そのop自体はこのエンドポイント
      # でのみ扱われ、/internal/boards/:id/ops へは決して送られない。
      # このエンドポイントは以前 params[:seq]（relayは既に送っていた）を
      # 一切使っておらず、board.last_seq が undo の分だけ取り残されたまま
      # 更新されていなかった。その結果、次に届く op の seq が
      # 「last_seq + 1」より必ず大きくなり、create_ops のgap検出
      # （本来は relay のクラッシュ等による本物の欠落opを検知するためのもの）が
      # undo のたびに誤発火し、以降の op が 409 gap_detected で永久に拒否・
      # 破棄される事故につながっていた（全消去が保存されない等）。
      # relay から届く seq で last_seq を追随させ、この取り残しを解消する。
      seq = params[:seq].to_i
      board.update!(last_seq: seq) if seq > board.last_seq

      render json: { ok: true }, status: :ok
    end

    private

    def render_not_owner
      render json: { error: "not_owner" }, status: :forbidden
    end

    def persist_op!(board, raw_op)
      kind = raw_op[:kind]
      op = BoardOp.new(
        op_id: raw_op[:op_id],
        session_id: raw_op[:session_key],
        board_id: board.id,
        seq: raw_op[:seq].to_i,
        kind: kind,
        undone: false
      )

      case kind
      when "stroke_add"
        stroke_attrs = raw_op[:stroke] || {}
        stroke = Stroke.create!(
          id: stroke_attrs[:id],
          session_id: raw_op[:session_key],
          board_id: board.id,
          tool: stroke_attrs[:tool],
          color: stroke_attrs[:color],
          width: stroke_attrs[:width]
        ) { |s| s.point_list = stroke_attrs[:points] || [] }
        op.stroke_id = stroke.id
      when "stroke_erase"
        # requirements.md 11.2節：対象が既に消去済みでもエラーにせず、そのまま操作ログに記録する。
        op.target_stroke_id_list = raw_op[:target_stroke_ids] || []
      end

      op.save!
    end

    def rate_limit_ok?(session_key, board_id)
      return true if session_key.blank?

      window_start = 1.second.ago
      recent_count = BoardOp.where(session_id: session_key, board_id: board_id)
                             .where("created_at >= ?", window_start)
                             .count
      recent_count < RATE_LIMIT_PER_SECOND
    end

    def render_not_found
      render json: { error: "not_found" }, status: :not_found
    end
  end
end
