require "rails_helper"

RSpec.describe "Api::V1::Boards", type: :request do
  def issue_session_key
    post "/api/v1/sessions"
    JSON.parse(response.body)["session_key"]
  end

  def auth_headers(key)
    { "X-Session-Key" => key }
  end

  describe "POST /api/v1/boards" do
    it "ボードを作成し、作成者の参加レコードも同時に作る（正常系）" do
      key = issue_session_key

      post "/api/v1/boards", params: { title: "打合せ用" }, headers: auth_headers(key), as: :json

      expect(response).to have_http_status(:created)
      body = JSON.parse(response.body)
      expect(body["title"]).to eq("打合せ用")
      expect(body["label"]).to eq("参加者A")
      expect(body["color"]).to eq("#1A1A1A")
      expect(body["board_token"]).to be_present
      # board_tokenはboard_id・session_idから導出しない（推測不可能性の独立確保。requirements.md 6章）
      expect(body["board_token"]).not_to include(body["board_id"])

      board = Board.find(body["board_id"])
      expect(board.participations.count).to eq(1)
    end

    it "ハニーポット項目に値がある場合は422で拒否し、ボードを作成しない" do
      key = issue_session_key

      post "/api/v1/boards", params: { title: "x", website: "http://spam.example" }, headers: auth_headers(key), as: :json

      expect(response).to have_http_status(:unprocessable_content)
      expect(JSON.parse(response.body)["error"]).to eq("invalid_request")
      expect(Board.count).to eq(0)
    end

    it "1セッションが作成できるボード数は20件までとする" do
      key = issue_session_key

      20.times { |i| post "/api/v1/boards", params: { title: "board#{i}" }, headers: auth_headers(key), as: :json }
      expect(response).to have_http_status(:created)

      post "/api/v1/boards", params: { title: "21th" }, headers: auth_headers(key), as: :json

      expect(response).to have_http_status(:unprocessable_content)
      expect(JSON.parse(response.body)["error"]).to eq("board_limit_exceeded")
      expect(Board.where(session_id: key).count).to eq(20)
    end

    it "X-Session-Keyヘッダが無い場合は401を返す" do
      post "/api/v1/boards", params: { title: "x" }, as: :json

      expect(response).to have_http_status(:unauthorized)
    end
  end

  describe "GET /api/v1/boards" do
    it "自分の参加レコードがあるボードのみ一覧に表示する" do
      key_a = issue_session_key
      key_b = issue_session_key

      post "/api/v1/boards", params: { title: "Aのボード" }, headers: auth_headers(key_a), as: :json
      post "/api/v1/boards", params: { title: "Bのボード" }, headers: auth_headers(key_b), as: :json

      get "/api/v1/boards", headers: auth_headers(key_a)

      expect(response).to have_http_status(:ok)
      boards = JSON.parse(response.body)["boards"]
      expect(boards.map { |b| b["title"] }).to eq(["Aのボード"])
      expect(boards.first["participant_count"]).to eq(1)
    end

    # Issue #30：一覧から入ったボードでも中継サーバーへ join できるようにするため、
    # board_token を返す必要がある。返していなかったため本番で join が成立せず、
    # 描画opが全て捨てられUndo/Redoも永久に無反応になっていた。
    it "board_tokenを含める（中継サーバーへのjoinに必須）" do
      key = issue_session_key
      post "/api/v1/boards", params: { title: "トークン確認" }, headers: auth_headers(key), as: :json
      created_token = JSON.parse(response.body)["board_token"]

      get "/api/v1/boards", headers: auth_headers(key)

      boards = JSON.parse(response.body)["boards"]
      expect(boards.first["board_token"]).to eq(created_token)
    end
  end

  describe "PATCH /api/v1/boards/:board_id" do
    it "参加者なら名称を変更できる" do
      key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(key), as: :json
      board_id = JSON.parse(response.body)["board_id"]

      patch "/api/v1/boards/#{board_id}", params: { title: "新しい名称" }, headers: auth_headers(key), as: :json

      expect(response).to have_http_status(:ok)
      expect(JSON.parse(response.body)["title"]).to eq("新しい名称")
    end

    it "参加レコードのないボードへは404を返す（存在の有無を漏らさない・最重要要件）" do
      owner_key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(owner_key), as: :json
      board_id = JSON.parse(response.body)["board_id"]

      stranger_key = issue_session_key
      patch "/api/v1/boards/#{board_id}", params: { title: "乗っ取り" }, headers: auth_headers(stranger_key), as: :json
      expect(response).to have_http_status(:not_found)
      expect(JSON.parse(response.body)["error"]).to eq("not_found")

      nonexistent_key = issue_session_key
      patch "/api/v1/boards/does-not-exist", params: { title: "x" }, headers: auth_headers(nonexistent_key), as: :json
      expect(response).to have_http_status(:not_found)
    end
  end

  describe "GET /api/v1/boards/by_token/:board_token（参加画面プレビュー）" do
    it "参加レコードを作らずボード情報を返す" do
      owner_key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(owner_key), as: :json
      board_json = JSON.parse(response.body)

      viewer_key = issue_session_key
      get "/api/v1/boards/by_token/#{board_json['board_token']}", headers: auth_headers(viewer_key)

      expect(response).to have_http_status(:ok)
      body = JSON.parse(response.body)
      expect(body["board_id"]).to eq(board_json["board_id"])
      expect(body["participant_count"]).to eq(1)
      expect(body["joinable"]).to eq(true)
      expect(Participation.where(session_id: viewer_key).count).to eq(0)
    end

    it "存在しないボードトークンには404を返す" do
      key = issue_session_key
      get "/api/v1/boards/by_token/does-not-exist", headers: auth_headers(key)

      expect(response).to have_http_status(:not_found)
    end
  end

  describe "POST /api/v1/boards/by_token/:board_token/join" do
    it "参加すると参加者ラベル・色・操作ログスナップショットを返す（冪等）" do
      owner_key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(owner_key), as: :json
      board_json = JSON.parse(response.body)

      participant_key = issue_session_key
      post "/api/v1/boards/by_token/#{board_json['board_token']}/join", headers: auth_headers(participant_key), as: :json

      expect(response).to have_http_status(:ok)
      body = JSON.parse(response.body)
      expect(body["label"]).to eq("参加者B")
      expect(body["ops"]).to eq([])

      # 同一セッションが再度参加要求しても、既存の参加レコードを使い増殖しない
      post "/api/v1/boards/by_token/#{board_json['board_token']}/join", headers: auth_headers(participant_key), as: :json
      expect(JSON.parse(response.body)["label"]).to eq("参加者B")
      expect(Participation.where(board_id: board_json["board_id"]).count).to eq(2)
    end

    it "参加者上限10人に達している場合は409を返す" do
      owner_key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(owner_key), as: :json
      board_json = JSON.parse(response.body)

      9.times do
        key = issue_session_key
        post "/api/v1/boards/by_token/#{board_json['board_token']}/join", headers: auth_headers(key), as: :json
      end
      expect(Participation.where(board_id: board_json["board_id"]).count).to eq(10)

      eleventh_key = issue_session_key
      post "/api/v1/boards/by_token/#{board_json['board_token']}/join", headers: auth_headers(eleventh_key), as: :json

      expect(response).to have_http_status(:conflict)
      expect(JSON.parse(response.body)["error"]).to eq("participant_limit")
    end
  end

  describe "GET /api/v1/boards/:board_id/ops" do
    it "参加者なら連番範囲でopsを取得できる" do
      owner_key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(owner_key), as: :json
      board_json = JSON.parse(response.body)
      board = Board.find(board_json["board_id"])

      stroke = Stroke.create!(id: "stroke-1", session_id: owner_key, board_id: board.id,
                               tool: "pen", color: "#1A1A1A", width: "medium") { |s| s.point_list = [[0, 0], [1, 1]] }
      BoardOp.create!(op_id: "op-1", session_id: owner_key, board_id: board.id, seq: 1,
                       kind: "stroke_add", stroke_id: stroke.id)
      board.update!(last_seq: 1, op_count: 1)

      get "/api/v1/boards/#{board.id}/ops", headers: auth_headers(owner_key)

      expect(response).to have_http_status(:ok)
      ops = JSON.parse(response.body)["ops"]
      expect(ops.length).to eq(1)
      expect(ops.first["op_id"]).to eq("op-1")
      expect(ops.first["author_label"]).to eq("参加者A")
      expect(ops.first["stroke"]["points"]).to eq([[0, 0], [1, 1]])
    end

    it "参加レコードのないボードへは404を返す" do
      owner_key = issue_session_key
      post "/api/v1/boards", params: {}, headers: auth_headers(owner_key), as: :json
      board_id = JSON.parse(response.body)["board_id"]

      stranger_key = issue_session_key
      get "/api/v1/boards/#{board_id}/ops", headers: auth_headers(stranger_key)

      expect(response).to have_http_status(:not_found)
    end
  end

  describe "Originヘッダの制限（requirements.md 28章）" do
    around do |example|
      original = ENV["ALLOWED_ORIGINS"]
      ENV["ALLOWED_ORIGINS"] = "http://localhost:3000"
      example.run
      ENV["ALLOWED_ORIGINS"] = original
    end

    it "許可リスト外のOriginヘッダは403で拒否する" do
      key = issue_session_key
      get "/api/v1/boards", headers: auth_headers(key).merge("Origin" => "http://evil.example")

      expect(response).to have_http_status(:forbidden)
    end

    it "許可リスト内のOriginヘッダは通す" do
      key = issue_session_key
      get "/api/v1/boards", headers: auth_headers(key).merge("Origin" => "http://localhost:3000")

      expect(response).to have_http_status(:ok)
    end
  end
end
