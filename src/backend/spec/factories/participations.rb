FactoryBot.define do
  factory :participation do
    association :session
    association :board
    label { "参加者A" }
    color { "#1A1A1A" }
    joined_at { Time.current }
    last_connected_at { Time.current }
  end
end
