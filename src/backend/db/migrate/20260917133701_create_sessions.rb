class CreateSessions < ActiveRecord::Migration[7.2]
  def change
    create_table :sessions, id: :string do |t|
      t.datetime :created_at, null: false
      t.datetime :last_seen_at, null: false
    end
  end
end
