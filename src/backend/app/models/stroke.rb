# requirements.md 9章・20章：ストロークの本体。点列はJSON配列 [[x,y],...] として保持する。
class Stroke < ApplicationRecord
  TOOLS = %w[pen marker].freeze
  WIDTHS = %w[thin medium thick].freeze
  MAX_POINTS = 1000

  belongs_to :session, inverse_of: :strokes
  belongs_to :board, inverse_of: :strokes

  validates :tool, inclusion: { in: TOOLS }
  validates :width, inclusion: { in: WIDTHS }

  # セキュリティレビュー修正（Medium）：INTEGRATION_CONTRACT.md 9章の8色許可リスト・
  # requirements.md 8.2節「1ストロークあたりの点数は1,000点を上限とする」は、
  # これまでGo中継サーバーのWebSocketメッセージ検証でのみ課されており、
  # 実際の永続化を担うRails側モデルには制約が無かった（中継のバグ・将来の呼び出し元追加で
  # 素通りする可能性があるため、最終防衛線としてモデル層にも同じ制約を課す）。
  validates :color, inclusion: { in: Participation::COLORS }
  validates :point_count, numericality: { less_than_or_equal_to: MAX_POINTS }

  def point_list
    return [] if points.blank?

    JSON.parse(points)
  end

  def point_list=(arr)
    list = Array(arr)
    self.points = list.to_json
    self.point_count = list.size
  end
end
