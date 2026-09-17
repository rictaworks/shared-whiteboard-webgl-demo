# requirements.md 6章・20章：全テーブルのオーナーキー。
# 推測不可能な不透明識別子をPKとして発行する（オートインクリメント整数PKにしない）。
class Session < ApplicationRecord
  # SecureRandom.urlsafe_base64(32) は 256bit 相当のエントロピー（最低要求の192bitを超える）。
  KEY_BYTES = 32

  has_many :boards, foreign_key: :session_id, inverse_of: :creator_session
  has_many :participations, foreign_key: :session_id, inverse_of: :session
  has_many :board_ops, foreign_key: :session_id, inverse_of: :session
  has_many :strokes, foreign_key: :session_id, inverse_of: :session

  before_validation :assign_defaults

  validates :id, presence: true, uniqueness: true

  def touch_last_seen!
    update_column(:last_seen_at, Time.current)
  end

  private

  def assign_defaults
    self.id ||= SecureRandom.urlsafe_base64(KEY_BYTES)
    self.last_seen_at ||= Time.current
  end
end
