class CreateParticipations < ActiveRecord::Migration[7.2]
  def change
    create_table :participations, id: :string do |t|
      t.string :session_id, null: false
      t.string :board_id, null: false
      t.string :label, null: false
      t.string :color, null: false
      t.datetime :joined_at, null: false
      t.datetime :last_connected_at, null: false
    end

    add_index :participations, %i[session_id board_id], unique: true
    add_index :participations, :board_id
  end
end
