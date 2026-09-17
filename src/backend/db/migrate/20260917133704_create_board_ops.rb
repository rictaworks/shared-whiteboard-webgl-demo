class CreateBoardOps < ActiveRecord::Migration[7.2]
  def change
    create_table :board_ops, primary_key: :op_id, id: :string do |t|
      t.string :session_id, null: false
      t.string :board_id, null: false
      t.integer :seq, null: false
      t.string :kind, null: false
      t.string :stroke_id
      t.text :target_stroke_ids
      t.boolean :undone, null: false, default: false
      t.datetime :created_at, null: false
    end

    add_index :board_ops, %i[board_id seq], unique: true
    add_index :board_ops, :session_id
    add_index :board_ops, :stroke_id
  end
end
