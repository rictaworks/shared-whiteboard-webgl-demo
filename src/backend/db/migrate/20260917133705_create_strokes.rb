class CreateStrokes < ActiveRecord::Migration[7.2]
  def change
    create_table :strokes, id: :string do |t|
      t.string :session_id, null: false
      t.string :board_id, null: false
      t.string :tool, null: false
      t.string :color, null: false
      t.string :width, null: false
      t.text :points, null: false
      t.integer :point_count, null: false
      t.datetime :created_at, null: false
    end

    add_index :strokes, :board_id
    add_index :strokes, :session_id
  end
end
