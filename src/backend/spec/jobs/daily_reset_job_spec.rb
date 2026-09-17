require "rails_helper"

RSpec.describe DailyResetJob do
  before do
    ENV["RELAY_INTERNAL_URL"] = "http://relay.example"
    ENV["INTERNAL_SHARED_SECRET"] = "test-internal-secret"

    stub_request(:post, "http://relay.example/internal/reset")
      .to_return(status: 200, body: { ok: true, hubs_closed: 0 }.to_json)
  end

  it "全テーブルを削除し、中継サーバーへ全ハブ破棄を要求する" do
    session = FactoryBot.create(:session)
    board = FactoryBot.create(:board, creator_session: session)
    FactoryBot.create(:participation, session: session, board: board)
    stroke = Stroke.create!(id: "stroke-1", session_id: session.id, board_id: board.id,
                             tool: "pen", color: "#1A1A1A", width: "medium") { |s| s.point_list = [[0, 0]] }
    BoardOp.create!(op_id: "op-1", session_id: session.id, board_id: board.id, seq: 1,
                     kind: "stroke_add", stroke_id: stroke.id)

    described_class.new.run

    expect(Session.count).to eq(0)
    expect(Board.count).to eq(0)
    expect(Participation.count).to eq(0)
    expect(Stroke.count).to eq(0)
    expect(BoardOp.count).to eq(0)
    expect(a_request(:post, "http://relay.example/internal/reset")).to have_been_made.once
  end

  it "中継サーバーへの通知が失敗してもデータ削除は必ず実行する" do
    stub_request(:post, "http://relay.example/internal/reset").to_timeout

    FactoryBot.create(:session)

    expect { described_class.new.run }.not_to raise_error
    expect(Session.count).to eq(0)
  end

  it "RELAY_INTERNAL_URLが未設定でも例外にせず、データ削除は実行する" do
    ENV.delete("RELAY_INTERNAL_URL")
    FactoryBot.create(:session)

    expect { described_class.new.run }.not_to raise_error
    expect(Session.count).to eq(0)
  end
end
