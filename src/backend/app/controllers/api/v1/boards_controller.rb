# INTEGRATION_CONTRACT.md 2章のエンドポイント群。所有権検証はOwnedByParticipationに委譲する。
module Api
  module V1
    class BoardsController < Api::BaseController
      include OwnedByParticipation

      # POST /api/v1/boards
      def create
        return render_honeypot_rejected if honeypot_present?
        return render_board_limit_exceeded if board_limit_reached?
        return render_invalid_title if title_too_long?(params[:title])

        board = nil
        participation = nil

        ActiveRecord::Base.transaction do
          board = Board.create!(session_id: current_session.id, title: params[:title].presence)
          participation = create_participation!(board)
        end

        render json: {
          board_id: board.id,
          board_token: board.board_token,
          title: board.title,
          label: participation.label,
          color: participation.color
        }, status: :created
      end

      # GET /api/v1/boards
      def index
        participations = Participation.where(session_id: current_session.id).includes(:board)

        boards = participations.map do |participation|
          board = participation.board
          {
            board_id: board.id,
            # board_token は中継サーバーへの join に必須（Issue #30）。返すのは
            # 自分が参加者であるボードだけなので、参加URLで既に渡しているものと
            # 同じ値を本人に返すことになり、新たな権限の露出は無い。
            board_token: board.board_token,
            title: board.title,
            updated_at: board.updated_at.iso8601,
            participant_count: Participation.where(board_id: board.id).count
          }
        end

        render json: { boards: boards }, status: :ok
      end

      # PATCH /api/v1/boards/:board_id
      def update
        participation = participation_for!(params[:board_id])
        return if participation.nil?

        return render_invalid_title if title_too_long?(params[:title])

        board = participation.board
        board.update!(title: params[:title].presence)
        render json: { board_id: board.id, title: board.title }, status: :ok
      end

      # GET /api/v1/boards/by_token/:board_token
      def show_by_token
        board = Board.find_by(board_token: params[:board_token])
        return render_board_not_found if board.nil?

        already_joined = Participation.exists?(session_id: current_session.id, board_id: board.id)
        participant_count = Participation.where(board_id: board.id).count
        joinable = already_joined || participant_count < Participation::MAX_PARTICIPANTS

        render json: {
          board_id: board.id,
          title: board.title,
          participant_count: participant_count,
          joinable: joinable
        }, status: :ok
      end

      # POST /api/v1/boards/by_token/:board_token/join
      def join
        board = Board.find_by(board_token: params[:board_token])
        return render_board_not_found if board.nil?

        participation = Participation.find_by(session_id: current_session.id, board_id: board.id)

        if participation
          participation.update!(last_connected_at: Time.current)
        else
          return render_participant_limit if participant_limit_reached?(board)

          participation = create_participation!(board)
        end

        render json: {
          board_id: board.id,
          title: board.title,
          label: participation.label,
          color: participation.color,
          last_seq: board.last_seq,
          ops: ordered_ops(board.id).map { |op| serialize_op(op) }
        }, status: :ok
      end

      # GET /api/v1/boards/:board_id/ops?from_seq=&to_seq=
      def ops
        participation = participation_for!(params[:board_id])
        return if participation.nil?

        from_seq = parse_seq_param(params[:from_seq], default: 0)
        return render_invalid_request if from_seq.nil?

        to_seq = nil
        if params[:to_seq].present?
          to_seq = parse_seq_param(params[:to_seq], default: nil)
          return render_invalid_request if to_seq.nil?
        end

        scope = BoardOp.where(board_id: params[:board_id]).where("seq > ?", from_seq)
        scope = scope.where("seq <= ?", to_seq) if to_seq
        board_ops = scope.order(:seq)

        render json: { ops: board_ops.map { |op| serialize_op(op) } }, status: :ok
      end

      private

      def honeypot_present?
        params[:website].present?
      end

      def board_limit_reached?
        Board.where(session_id: current_session.id).count >= Board::MAX_BOARDS_PER_SESSION
      end

      def participant_limit_reached?(board)
        Participation.where(board_id: board.id).count >= Participation::MAX_PARTICIPANTS
      end

      def title_too_long?(title)
        title.present? && title.to_s.length > Board::MAX_TITLE_LENGTH
      end

      def ordered_ops(board_id)
        BoardOp.where(board_id: board_id).order(:seq)
      end

      def create_participation!(board)
        used_labels = Participation.where(board_id: board.id).pluck(:label)
        label = Participation::LABELS.find { |candidate| !used_labels.include?(candidate) } || Participation::LABELS.last
        color = Participation::COLORS[used_labels.size % Participation::COLORS.length]

        Participation.create!(
          session_id: current_session.id,
          board_id: board.id,
          label: label,
          color: color,
          joined_at: Time.current,
          last_connected_at: Time.current
        )
      end

      def parse_seq_param(value, default:)
        return default if value.blank?

        Integer(value)
      rescue ArgumentError, TypeError
        nil
      end

      def serialize_op(op)
        result = {
          op_id: op.op_id,
          seq: op.seq,
          kind: op.kind,
          author_label: author_label_for(op),
          undone: op.undone
        }

        case op.kind
        when "stroke_add"
          result[:stroke] = serialize_stroke(op.stroke)
        when "stroke_erase"
          result[:target_stroke_ids] = op.target_stroke_id_list
        end

        result
      end

      def author_label_for(op)
        Participation.find_by(session_id: op.session_id, board_id: op.board_id)&.label
      end

      def serialize_stroke(stroke)
        return nil if stroke.nil?

        {
          id: stroke.id,
          tool: stroke.tool,
          color: stroke.color,
          width: stroke.width,
          points: stroke.point_list
        }
      end

      def render_honeypot_rejected
        render json: { error: "invalid_request" }, status: :unprocessable_content
      end

      def render_board_limit_exceeded
        render json: { error: "board_limit_exceeded" }, status: :unprocessable_content
      end

      def render_invalid_title
        render json: { error: "invalid_request" }, status: :unprocessable_content
      end

      def render_invalid_request
        render json: { error: "invalid_request" }, status: :unprocessable_content
      end

      def render_board_not_found
        render json: { error: "not_found" }, status: :not_found
      end

      def render_participant_limit
        render json: { error: "participant_limit" }, status: :conflict
      end
    end
  end
end
