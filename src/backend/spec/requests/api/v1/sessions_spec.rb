require "rails_helper"

RSpec.describe "Api::V1::Sessions", type: :request do
  describe "POST /api/v1/sessions" do
    it "推測不可能なセッションキーを発行する（ヘッダ・ボディ不要）" do
      post "/api/v1/sessions"

      expect(response).to have_http_status(:created)
      body = JSON.parse(response.body)
      expect(body["session_key"]).to be_a(String)
      expect(body["session_key"].length).to be >= 32
      expect(Session.find_by(id: body["session_key"])).to be_present
    end

    it "呼ぶたびに異なるセッションキーを発行する" do
      post "/api/v1/sessions"
      first_key = JSON.parse(response.body)["session_key"]

      post "/api/v1/sessions"
      second_key = JSON.parse(response.body)["session_key"]

      expect(first_key).not_to eq(second_key)
    end
  end
end
