# This file is auto-generated from the current state of the database. Instead
# of editing this file, please use the migrations feature of Active Record to
# incrementally modify your database, and then regenerate this schema definition.
#
# This file is the source Rails uses to define your schema when running `bin/rails
# db:schema:load`. When creating a new database, `bin/rails db:schema:load` tends to
# be faster and is potentially less error prone than running all of your
# migrations from scratch. Old migrations may fail to apply correctly if those
# migrations use external dependencies or application code.
#
# It's strongly recommended that you check this file into your version control system.

ActiveRecord::Schema[7.2].define(version: 2026_09_17_133705) do
  create_table "board_ops", primary_key: "op_id", id: :string, force: :cascade do |t|
    t.string "session_id", null: false
    t.string "board_id", null: false
    t.integer "seq", null: false
    t.string "kind", null: false
    t.string "stroke_id"
    t.text "target_stroke_ids"
    t.boolean "undone", default: false, null: false
    t.datetime "created_at", null: false
    t.index ["board_id", "seq"], name: "index_board_ops_on_board_id_and_seq", unique: true
    t.index ["session_id"], name: "index_board_ops_on_session_id"
    t.index ["stroke_id"], name: "index_board_ops_on_stroke_id"
  end

  create_table "boards", id: :string, force: :cascade do |t|
    t.string "session_id", null: false
    t.string "board_token", null: false
    t.string "title"
    t.integer "last_seq", default: 0, null: false
    t.integer "op_count", default: 0, null: false
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["board_token"], name: "index_boards_on_board_token", unique: true
    t.index ["session_id"], name: "index_boards_on_session_id"
  end

  create_table "participations", id: :string, force: :cascade do |t|
    t.string "session_id", null: false
    t.string "board_id", null: false
    t.string "label", null: false
    t.string "color", null: false
    t.datetime "joined_at", null: false
    t.datetime "last_connected_at", null: false
    t.index ["board_id"], name: "index_participations_on_board_id"
    t.index ["session_id", "board_id"], name: "index_participations_on_session_id_and_board_id", unique: true
  end

  create_table "sessions", id: :string, force: :cascade do |t|
    t.datetime "created_at", null: false
    t.datetime "last_seen_at", null: false
  end

  create_table "strokes", id: :string, force: :cascade do |t|
    t.string "session_id", null: false
    t.string "board_id", null: false
    t.string "tool", null: false
    t.string "color", null: false
    t.string "width", null: false
    t.text "points", null: false
    t.integer "point_count", null: false
    t.datetime "created_at", null: false
    t.index ["board_id"], name: "index_strokes_on_board_id"
    t.index ["session_id"], name: "index_strokes_on_session_id"
  end
end
