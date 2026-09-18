require "rails_helper"

RSpec.describe "Internal::System", type: :request do
  let(:internal_secret) { "test-internal-secret" }

  before do
    ENV["INTERNAL_SHARED_SECRET"] = internal_secret
    ENV["RELAY_INTERNAL_URL"] = "http://relay.example"
    stub_request(:post, "http://relay.example/internal/reset")
      .to_return(status: 200, body: { ok: true, hubs_closed: 0 }.to_json)
  end

  def internal_headers
    { "X-Internal-Secret" => internal_secret }
  end

  describe "POST /internal/system/daily_reset" do
    it "認証ヘッダが正しければ全テーブルを削除する" do
      session = FactoryBot.create(:session)
      FactoryBot.create(:board, creator_session: session)

      post "/internal/system/daily_reset", headers: internal_headers

      expect(response).to have_http_status(:ok)
      expect(Session.count).to eq(0)
      expect(Board.count).to eq(0)
      expect(a_request(:post, "http://relay.example/internal/reset")).to have_been_made.once
    end

    it "認証ヘッダが無いと拒否され、データは削除されない" do
      FactoryBot.create(:session)

      post "/internal/system/daily_reset"

      expect(response).to have_http_status(:unauthorized)
      expect(Session.count).to eq(1)
    end

    it "認証ヘッダの値が誤っていると拒否される" do
      post "/internal/system/daily_reset", headers: { "X-Internal-Secret" => "wrong-secret" }

      expect(response).to have_http_status(:unauthorized)
    end
  end
end
