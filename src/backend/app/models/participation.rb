# requirements.md 6章・15章：セッションがボードトークンによりボードに加わった状態。
# 参加者ラベル・識別色は参加順に割り当て、離脱後も再利用しない（レコードを削除しないため自然に満たされる）。
class Participation < ApplicationRecord
  MAX_PARTICIPANTS = 10

  LABELS = (0...10).map { |i| "参加者#{("A".ord + i).chr}" }.freeze

  # INTEGRATION_CONTRACT.md 9章のマスタデータと一致させること
  COLORS = %w[
    #1A1A1A
    #E53935
    #1E88E5
    #2E7D32
    #F9A825
    #8E24AA
    #FF6F00
    #546E7A
  ].freeze

  belongs_to :session, inverse_of: :participations
  belongs_to :board, inverse_of: :participations

  validates :label, presence: true
  validates :color, presence: true
  validates :session_id, uniqueness: { scope: :board_id }

  before_validation :assign_id

  private

  def assign_id
    self.id ||= SecureRandom.uuid
  end
end
