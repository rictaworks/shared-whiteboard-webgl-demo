using System;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 11.4章。連番連続性を検証し、欠番を検出した場合は後続を適用せず再取得を要求する。
    /// </summary>
    public class SeqGuard
    {
        public int Expected { get; private set; } = 1;

        public event Action<int, int> RefetchRequested;

        public void SetExpected(int expected)
        {
            Expected = expected;
        }

        /// <summary>
        /// 受信したseqを検証する。連続していれば true を返し Expected を進める。
        /// 既知（重複・過去分）は false を返すが再取得は要求しない。
        /// 欠番の場合は false を返し、再取得（Expected〜seq-1）を要求する。
        /// </summary>
        public bool Accept(int seq)
        {
            if (seq < Expected)
            {
                return false;
            }
            if (seq == Expected)
            {
                Expected = seq + 1;
                return true;
            }

            RequestRefetch(Expected, seq - 1);
            return false;
        }

        public void RequestRefetch(int fromSeq, int toSeq)
        {
            RefetchRequested?.Invoke(fromSeq, toSeq);
        }
    }
}
