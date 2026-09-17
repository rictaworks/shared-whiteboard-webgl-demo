require "rails_helper"

RSpec.describe "Internal::VerifyParticipation", type: :request do
  let(:internal_secret) { "test-internal-secret" }

  before { ENV["INTERNAL_SHARED_SECRET"] = internal_secret }

  def internal_headers
    { "X-Internal-Secret" => internal_secret }
  end

  it "参加レコードが一致すればok:trueとラベル・色を返す" do
    session = FactoryBot.create(:session)
    board = FactoryBot.create(:board, creator_session: session)
    participation = FactoryBot.create(:participation, session: session, board: board, label: "参加者A", color: "#1A1A1A")

    post "/internal/verify_participation",
      params: { session_key: session.id, board_token: board.board_token },
      headers: internal_headers,
      as: :json

    expect(response).to have_http_status(:ok)
    body = JSON.parse(response.body)
    expect(body["ok"]).to eq(true)
    expect(body["label"]).to eq(participation.label)
    expect(body["board_id"]).to eq(board.id)
  end

  it "ボードが存在しない場合はHTTP200のままok:false, reason:not_foundを返す" do
    post "/internal/verify_participation",
      params: { session_key: "x", board_token: "does-not-exist" },
      headers: internal_headers,
      as: :json

    expect(response).to have_http_status(:ok)
    body = JSON.parse(response.body)
    expect(body["ok"]).to eq(false)
    expect(body["reason"]).to eq("not_found")
  end

  it "参加レコードが無い場合はok:false, reason:verification_failedを返す" do
    session = FactoryBot.create(:session)
    board = FactoryBot.create(:board, creator_session: session)

    post "/internal/verify_participation",
      params: { session_key: session.id, board_token: board.board_token },
      headers: internal_headers,
      as: :json

    body = JSON.parse(response.body)
    expect(body["ok"]).to eq(false)
    expect(body["reason"]).to eq("verification_failed")
  end

  it "X-Internal-Secretが一致しない場合は401を返す" do
    post "/internal/verify_participation",
      params: { session_key: "x", board_token: "y" },
      headers: { "X-Internal-Secret" => "wrong-secret" },
      as: :json

    expect(response).to have_http_status(:unauthorized)
  end

  it "X-Internal-Secretヘッダが無い場合は401を返す" do
    post "/internal/verify_participation", params: { session_key: "x", board_token: "y" }, as: :json

    expect(response).to have_http_status(:unauthorized)
  end
end
