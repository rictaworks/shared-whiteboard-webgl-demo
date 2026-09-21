require "rails_helper"

RSpec.describe "Internal::Boards", type: :request do
  let(:internal_secret) { "test-internal-secret" }
  let(:session) { FactoryBot.create(:session) }
  let(:board) { FactoryBot.create(:board, creator_session: session) }

  before { ENV["INTERNAL_SHARED_SECRET"] = internal_secret }

  def internal_headers
    { "X-Internal-Secret" => internal_secret }
  end

  describe "POST /internal/boards/:board_id/ops" do
    it "stroke_add操作を永続化し、last_seq・op_countを進める" do
      op = {
        op_id: "op-1",
        session_key: session.id,
        seq: 1,
        kind: "stroke_add",
        stroke: { id: "stroke-1", tool: "pen", color: "#1A1A1A", width: "medium", points: [[0, 0], [10, 10]] }
      }

      post "/internal/boards/#{board.id}/ops", params: { ops: [op] }, headers: internal_headers, as: :json

      expect(response).to have_http_status(:ok)
      expect(JSON.parse(response.body)["accepted"]).to eq(1)

      board.reload
      expect(board.last_seq).to eq(1)
      expect(board.op_count).to eq(1)
      expect(BoardOp.find_by(op_id: "op-1").kind).to eq("stroke_add")
      expect(Stroke.find("stroke-1").point_list).to eq([[0, 0], [10, 10]])
    end

    it "連番の重複は無視される（冪等性。同一op_id・同一seqの再送で重複登録されない）" do
      op = { op_id: "op-1", session_key: session.id, seq: 1, kind: "clear" }

      post "/internal/boards/#{board.id}/ops", params: { ops: [op] }, headers: internal_headers, as: :json
      expect(JSON.parse(response.body)["accepted"]).to eq(1)

      post "/internal/boards/#{board.id}/ops", params: { ops: [op] }, headers: internal_headers, as: :json
      expect(response).to have_http_status(:ok)
      expect(JSON.parse(response.body)["accepted"]).to eq(0)
      expect(BoardOp.where(board_id: board.id).count).to eq(1)
    end

    it "同じop_idが新しいseqで再送されても500にならず冪等に処理する（Issue #40の回帰テスト）" do
      # 実機で起きた事故の再現：relay再デプロイでプロセスメモリ上の
      # op冪等性マップが失われ、クライアントの未送信キューに残っていた
      # 「既に永続化済みのop」が、中継サーバーによって新しいseqで再送された。
      # 同じstroke idで2回目のINSERTを試みてUNIQUE制約違反（500）になり、
      # relayが無限リトライを続ける事故につながった。
      stroke = { id: "stroke-dup", tool: "pen", color: "#1A1A1A", width: "medium", points: [[0, 0], [10, 10]] }
      first = { op_id: "op-dup", session_key: session.id, seq: 1, kind: "stroke_add", stroke: stroke }
      post "/internal/boards/#{board.id}/ops", params: { ops: [first] }, headers: internal_headers, as: :json
      expect(JSON.parse(response.body)["accepted"]).to eq(1)

      # 別のopを挟んでlast_seqを進める（relay再起動を挟んだ想定）。
      bump = { op_id: "op-bump", session_key: session.id, seq: 2, kind: "clear" }
      post "/internal/boards/#{board.id}/ops", params: { ops: [bump] }, headers: internal_headers, as: :json
      expect(JSON.parse(response.body)["accepted"]).to eq(1)

      # 同じop_id・同じstroke idだが、last_seqより大きい新しいseq(3)で再送される。
      resend = { op_id: "op-dup", session_key: session.id, seq: 3, kind: "stroke_add", stroke: stroke }
      post "/internal/boards/#{board.id}/ops", params: { ops: [resend] }, headers: internal_headers, as: :json

      expect(response).to have_http_status(:ok)
      expect(JSON.parse(response.body)["accepted"]).to eq(0)

      board.reload
      expect(board.last_seq).to eq(3) # seqは消費されて前進する（gap検出との整合性のため）
      expect(board.op_count).to eq(2) # op_countは二重加算されない
      expect(BoardOp.where(op_id: "op-dup").count).to eq(1)
      expect(Stroke.where(id: "stroke-dup").count).to eq(1)
    end

    it "欠番を検出した場合は409 gap_detectedを返し、書き込まない" do
      op = { op_id: "op-3", session_key: session.id, seq: 3, kind: "clear" }

      post "/internal/boards/#{board.id}/ops", params: { ops: [op] }, headers: internal_headers, as: :json

      expect(response).to have_http_status(:conflict)
      body = JSON.parse(response.body)
      expect(body["error"]).to eq("gap_detected")
      expect(body["expected_from"]).to eq(1)
      expect(BoardOp.count).to eq(0)
    end

    it "バッチの中間に欠番がある場合も409 gap_detectedを返し、欠番より手前だけを確定する（reviewer修正の回帰テスト）" do
      # last_seq=0の状態で [seq=1, seq=2, seq=4] が届く（seq=3が欠落）。
      # バッチ先頭（min_seq=1）だけを見ていた旧実装ではここを検出できず、
      # seq=4まで誤って確定してしまっていた。
      ops = [
        { op_id: "op-1", session_key: session.id, seq: 1, kind: "clear" },
        { op_id: "op-2", session_key: session.id, seq: 2, kind: "clear" },
        { op_id: "op-4", session_key: session.id, seq: 4, kind: "clear" }
      ]

      post "/internal/boards/#{board.id}/ops", params: { ops: ops }, headers: internal_headers, as: :json

      expect(response).to have_http_status(:conflict)
      body = JSON.parse(response.body)
      expect(body["error"]).to eq("gap_detected")
      expect(body["expected_from"]).to eq(3)

      board.reload
      expect(board.last_seq).to eq(2)
      expect(BoardOp.where(board_id: board.id).pluck(:seq).sort).to eq([1, 2])
      expect(BoardOp.exists?(op_id: "op-4")).to eq(false)
    end

    it "stroke_eraseの対象が既に消去済みでもエラーにせず操作ログへ記録する" do
      erase1 = { op_id: "op-1", session_key: session.id, seq: 1, kind: "stroke_erase", target_stroke_ids: ["stroke-x"] }
      erase2 = { op_id: "op-2", session_key: session.id, seq: 2, kind: "stroke_erase", target_stroke_ids: ["stroke-x"] }

      post "/internal/boards/#{board.id}/ops", params: { ops: [erase1] }, headers: internal_headers, as: :json
      expect(response).to have_http_status(:ok)

      post "/internal/boards/#{board.id}/ops", params: { ops: [erase2] }, headers: internal_headers, as: :json
      expect(response).to have_http_status(:ok)
      expect(JSON.parse(response.body)["accepted"]).to eq(1)
      expect(BoardOp.where(board_id: board.id, kind: "stroke_erase").count).to eq(2)
    end

    it "操作数上限5000に達している場合は409 op_limit_exceededを返す" do
      board.update!(last_seq: 10, op_count: Board::MAX_OPS)
      op = { op_id: "op-11", session_key: session.id, seq: 11, kind: "clear" }

      post "/internal/boards/#{board.id}/ops", params: { ops: [op] }, headers: internal_headers, as: :json

      expect(response).to have_http_status(:conflict)
      expect(JSON.parse(response.body)["error"]).to eq("op_limit_exceeded")
    end

    it "ボードが存在しない場合は404を返す" do
      post "/internal/boards/does-not-exist/ops", params: { ops: [] }, headers: internal_headers, as: :json

      expect(response).to have_http_status(:not_found)
    end

    it "X-Internal-Secretが無い場合は401を返す" do
      post "/internal/boards/#{board.id}/ops", params: { ops: [] }, as: :json

      expect(response).to have_http_status(:unauthorized)
    end
  end

  describe "GET /internal/boards/:board_id/last_seq" do
    it "中継サーバー再起動時の採番再開用に現在の連番を返す" do
      board.update!(last_seq: 42)

      get "/internal/boards/#{board.id}/last_seq", headers: internal_headers

      expect(response).to have_http_status(:ok)
      expect(JSON.parse(response.body)["last_seq"]).to eq(42)
    end
  end

  describe "POST /internal/boards/:board_id/undo_flag" do
    it "取消フラグを更新する" do
      op = BoardOp.create!(op_id: "op-1", session_id: session.id, board_id: board.id, seq: 1, kind: "clear", undone: false)

      post "/internal/boards/#{board.id}/undo_flag",
        params: { op_id: op.op_id, undone: true, session_key: session.id, seq: 1 },
        headers: internal_headers,
        as: :json

      expect(response).to have_http_status(:ok)
      expect(op.reload.undone).to eq(true)
    end

    it "存在しない操作IDには404を返す" do
      post "/internal/boards/#{board.id}/undo_flag",
        params: { op_id: "does-not-exist", undone: true },
        headers: internal_headers,
        as: :json

      expect(response).to have_http_status(:not_found)
    end

    it "セキュリティレビュー修正：自分以外のsession_keyでは他人の操作のundoフラグを変更できない（requirements.md 12章）" do
      owner_op = BoardOp.create!(op_id: "op-owner", session_id: session.id, board_id: board.id, seq: 1, kind: "clear", undone: false)
      stranger = FactoryBot.create(:session)

      post "/internal/boards/#{board.id}/undo_flag",
        params: { op_id: owner_op.op_id, undone: true, session_key: stranger.id, seq: 1 },
        headers: internal_headers,
        as: :json

      expect(response).to have_http_status(:forbidden)
      expect(JSON.parse(response.body)["error"]).to eq("not_owner")
      expect(owner_op.reload.undone).to eq(false)
    end

    it "session_keyを省略した場合も他人の操作へは作用しない（fail closed）" do
      owner_op = BoardOp.create!(op_id: "op-owner2", session_id: session.id, board_id: board.id, seq: 2, kind: "clear", undone: false)

      post "/internal/boards/#{board.id}/undo_flag",
        params: { op_id: owner_op.op_id, undone: true },
        headers: internal_headers,
        as: :json

      expect(response).to have_http_status(:forbidden)
      expect(owner_op.reload.undone).to eq(false)
    end
  end
end
