FactoryBot.define do
  factory :board do
    association :creator_session, factory: :session
    title { "テストボード" }
  end
end
