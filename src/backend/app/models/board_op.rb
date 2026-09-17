# requirements.md 11章・20章：ボードに対する操作ログ（追記型・連番付き）。
# PKはop_id（端末側で採番された値をそのまま使う。中継サーバー経由でRailsへ届く）。
class BoardOp < ApplicationRecord
  self.table_name = "board_ops"
  self.primary_key = "op_id"

  KINDS = %w[stroke_add stroke_erase clear].freeze

  belongs_to :session, inverse_of: :board_ops
  belongs_to :board, inverse_of: :board_ops
  belongs_to :stroke, optional: true, inverse_of: false

  validates :kind, inclusion: { in: KINDS }

  def target_stroke_id_list
    return [] if target_stroke_ids.blank?

    JSON.parse(target_stroke_ids)
  end

  def target_stroke_id_list=(ids)
    self.target_stroke_ids = Array(ids).to_json
  end
end
