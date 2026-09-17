# requirements.md 17章・20章：ボード1枚。board_tokenは参加URL用の不透明識別子で、
# board.id・session_idからは導出しない（推測不可能性を独立に確保する）。
class Board < ApplicationRecord
  MAX_BOARDS_PER_SESSION = 20
  MAX_OPS = 5000
  MAX_TITLE_LENGTH = 200
  TOKEN_BYTES = 32

  belongs_to :creator_session, class_name: "Session", foreign_key: :session_id, inverse_of: :boards
  has_many :participations, foreign_key: :board_id, inverse_of: :board
  has_many :board_ops, foreign_key: :board_id, inverse_of: :board
  has_many :strokes, foreign_key: :board_id, inverse_of: :board

  validates :title, length: { maximum: MAX_TITLE_LENGTH }, allow_blank: true
  validates :board_token, presence: true, uniqueness: true

  before_validation :assign_defaults

  private

  def assign_defaults
    self.id ||= SecureRandom.uuid
    self.board_token ||= SecureRandom.urlsafe_base64(TOKEN_BYTES)
    self.last_seq ||= 0
    self.op_count ||= 0
  end
end
