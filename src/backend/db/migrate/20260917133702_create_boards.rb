class CreateBoards < ActiveRecord::Migration[7.2]
  def change
    create_table :boards, id: :string do |t|
      t.string :session_id, null: false
      t.string :board_token, null: false
      t.string :title
      t.integer :last_seq, null: false, default: 0
      t.integer :op_count, null: false, default: 0
      t.timestamps
    end

    add_index :boards, :session_id
    add_index :boards, :board_token, unique: true
  end
end
