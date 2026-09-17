using System;
using System.Collections.Generic;
using UnityEngine;
using Whiteboard.Bridges;
using Whiteboard.Data;
using Whiteboard.Json;

namespace Whiteboard.Sync
{
    /// <summary>
    /// requirements.md 25.1章の状態遷移図に従う接続状態。
    /// </summary>
    public enum ConnState
    {
        Joining,
        Synced,
        Pending,
        Hidden,
        Reconnecting,
        Disconnected,
        Lost,
        Rejected,
    }

    /// <summary>
    /// requirements.md 11・14・23章。WebSocket接続管理・状態遷移・メッセージ送受信を担う。
    /// NetBridgeはfire-and-forget/ポーリング型のため、毎フレーム PumpMessages() / Tick() を呼ぶこと。
    /// </summary>
    public class SyncClient
    {
        public ConnState State { get; private set; } = ConnState.Disconnected;
        public string MyLabel { get; private set; }
        public string MyColor { get; private set; }

        public event Action JoinAccepted;
        public event Action<string> FatalReceived; // reason
        public event Action<Op, int> OpConfirmed; // op, seq
        public event Action<string, bool, int> UndoFlagConfirmed; // opId, undone, seq
        public event Action<string, List<Vector2>, string, ToolKind, StrokeWidth> RemoteActiveDelta; // strokeId, points, colorHex, tool, width
        public event Action<string, int> AckReceived; // opId, seq
        public event Action<string> RemoteActiveAbort; // strokeId
        public event Action<string, Vector2, string> RemoteCursor; // label, pos, color
        public event Action<List<string>, List<string>> PresenceChanged; // joined, left
        public event Action<int, int> RefetchRequired; // fromSeq, toSeq
        public event Action StateChanged;

        private readonly SendQueue _sendQueue;
        private readonly SeqGuard _seqGuard;

        private string _wsUrl;
        private bool _pendingJoin;
        private int _pendingJoinLastSeq;
        private bool _everConnectedOnce;

        private float _reconnectDelaySec = 1f;
        private const float MaxReconnectDelaySec = 30f;
        private const float MaxReconnectElapsedSec = 120f; // 規定時間の経過でDisconnectedへ
        private float _reconnectElapsed;
        private float _reconnectCountdown;
        private bool _hiddenNow;

        public SyncClient(SendQueue sendQueue, SeqGuard seqGuard)
        {
            _sendQueue = sendQueue;
            _seqGuard = seqGuard;
        }

        private void SetState(ConnState s)
        {
            if (State == s)
            {
                return;
            }
            State = s;
            StateChanged?.Invoke();
        }

        public void Connect(string wsUrl, int lastSeq)
        {
            _wsUrl = wsUrl;
            _pendingJoin = true;
            _pendingJoinLastSeq = lastSeq;
            _reconnectDelaySec = 1f;
            _reconnectElapsed = 0f;
            SetState(ConnState.Joining);
            NetBridge.Connect(wsUrl);
        }

        public void Reconnect()
        {
            SetState(ConnState.Reconnecting);
            _pendingJoin = true;
            _pendingJoinLastSeq = _seqGuard.Expected - 1;
            NetBridge.Connect(_wsUrl);
        }

        public void OnVisibilityChange(bool visible)
        {
            _hiddenNow = !visible;
            if (!visible)
            {
                if (State == ConnState.Synced || State == ConnState.Pending)
                {
                    SetState(ConnState.Hidden);
                }
            }
            else
            {
                if (State == ConnState.Hidden)
                {
                    // 蓄積分は次のPumpMessagesで処理される。接続断があればReconnectingへ。
                    SetState(_sendQueue.Pending.Count > 0 ? ConnState.Pending : ConnState.Synced);
                }
            }
        }

        public void SendActiveDelta(string strokeId, ToolKind tool, string colorHex, StrokeWidth width, List<Vector2> points)
        {
            var msg = new Dictionary<string, object>
            {
                ["type"] = "active_delta",
                ["stroke_id"] = strokeId,
                ["tool"] = MasterData.ToolToString(tool),
                ["color"] = colorHex,
                ["width"] = MasterData.WidthToString(width),
                ["points"] = PointsToJson(points),
            };
            Send(msg);
        }

        public void SendActiveAbort(string strokeId)
        {
            Send(new Dictionary<string, object> { ["type"] = "active_abort", ["stroke_id"] = strokeId });
        }

        public void SendOp(Op op)
        {
            _sendQueue.Enqueue(op);
            _sendQueue.Persist();
            SendOpMessage(op);
            RefreshPendingSnapshot();
            if (State == ConnState.Synced)
            {
                SetState(ConnState.Pending);
            }
        }

        private void SendOpMessage(Op op)
        {
            Send(BuildOpMessage(op));
        }

        private static Dictionary<string, object> BuildOpMessage(Op op)
        {
            var msg = new Dictionary<string, object> { ["type"] = "op", ["op_id"] = op.OpId, ["kind"] = OpKindUtil.ToStringValue(op.Kind) };
            if (op.Kind == OpKind.StrokeAdd && op.StrokeData != null)
            {
                var full = op.ToDict();
                msg["stroke"] = full["stroke"];
            }
            if (op.Kind == OpKind.StrokeErase)
            {
                var ids = new List<object>();
                if (op.TargetStrokeIds != null)
                {
                    foreach (var id in op.TargetStrokeIds)
                    {
                        ids.Add(id);
                    }
                }
                msg["target_stroke_ids"] = ids;
            }
            return msg;
        }

        /// <summary>
        /// requirements.md 7.3章・14章：「タブの終了時はUnityの処理を待たず、未送信の操作を
        /// 通信ブリッジから直接送出する」ための、NetBridge.jslib側スナップショットの更新。
        /// セキュリティ/堅牢性レビュー（2026-09-17）で追加：以前は WB_Net_SetPendingSnapshot が
        /// どこからも呼ばれておらず、NetBridge.jslib の pagehide ハンドラの直接送出（Unityの
        /// フレーム更新を待たない経路）が常に no-op になっていた（pendingSnapshot が undefined
        /// のまま）。未送信キューが変化するたびに（送信時・受領応答時・復元時）このスナップショットを
        /// 最新化し、ページが閉じられる直前でも直接送出が機能するようにする。
        /// </summary>
        public void RefreshPendingSnapshot()
        {
            var msgs = new List<object>();
            foreach (var op in _sendQueue.Pending)
            {
                msgs.Add(BuildOpMessage(op));
            }
            NetBridge.SetPendingSnapshot(MiniJson.Serialize(msgs));
        }

        public void SendUndoFlag(string opId, bool undone)
        {
            Send(new Dictionary<string, object> { ["type"] = "undo_flag", ["op_id"] = opId, ["undone"] = undone });
        }

        public void SendCursor(Vector2 p)
        {
            Send(new Dictionary<string, object> { ["type"] = "cursor", ["x"] = (double)p.x, ["y"] = (double)p.y });
        }

        private void Send(Dictionary<string, object> msg)
        {
            NetBridge.Send(MiniJson.Serialize(msg));
        }

        /// <summary>
        /// ページ終了直前：未送信の操作を通信ブリッジから直接送出する（Unity側のフレーム更新が
        /// 呼ばれた場合の経路）。実際に「Unityの処理を待たない」経路は NetBridge.jslib 自身の
        /// pagehide ハンドラであり、そちらは RefreshPendingSnapshot() で最新化した
        /// pendingSnapshot を使う。この呼び出しは Unity 側からの二重の安全策。
        /// </summary>
        public void FlushOnPageHide()
        {
            var msgs = new List<object>();
            foreach (var op in _sendQueue.ResendAll())
            {
                msgs.Add(BuildOpMessage(op));
            }
            NetBridge.SendOnPageHide(MiniJson.Serialize(msgs));
        }

        /// <summary>毎フレーム呼び出す。受信メッセージの一括取り出し・処理を行う。</summary>
        public void PumpMessages()
        {
            string json = NetBridge.Drain();

            // セキュリティレビュー（2026-09-17）：受信メッセージの形式を無条件に信頼しない。
            // NetBridge.jslib はWebSocketで受け取った文字列をそのままバッファへ積むだけで、
            // JSONとして妥当かの検証をしていない（中継サーバーの再配信を信頼する設計だが、
            // 回線異常・中継側の不具合等で壊れたペイロードが来ても、Unity側の受信ループ全体を
            // 落とさない防御的実装とする）。バッチ全体の解析・個別メッセージの解析をそれぞれ
            // try/catchで囲み、1件の異常メッセージが残りのメッセージ処理を止めないようにする。
            List<object> arr = null;
            try
            {
                arr = MiniJson.Deserialize(json) as List<object>;
            }
            catch (Exception)
            {
                arr = null; // バッチ全体が壊れている場合は今回分を諦め、次回のDrainに委ねる。
            }

            if (arr != null)
            {
                foreach (var item in arr)
                {
                    try
                    {
                        var raw = item as string;
                        Dictionary<string, object> d = null;
                        if (raw != null)
                        {
                            d = MiniJson.Deserialize(raw) as Dictionary<string, object>;
                        }
                        else if (item is Dictionary<string, object> dd)
                        {
                            d = dd;
                        }
                        if (d != null)
                        {
                            HandleMessage(d);
                        }
                    }
                    catch (Exception)
                    {
                        // 個別メッセージの解析・処理失敗は破棄し、他のメッセージの処理を継続する。
                    }
                }
            }

            if (NetBridge.Overflowed())
            {
                // 直近バッファで補完できない：受信済み連番から再取得。
                RefetchRequired?.Invoke(_seqGuard.Expected, -1);
            }
        }

        /// <summary>毎フレーム呼び出す。再接続の指数バックオフを進める。</summary>
        public void Tick(float deltaTime)
        {
            if (State != ConnState.Reconnecting)
            {
                return;
            }
            _reconnectElapsed += deltaTime;
            if (_reconnectElapsed > MaxReconnectElapsedSec)
            {
                SetState(ConnState.Disconnected);
                return;
            }
            _reconnectCountdown -= deltaTime;
            if (_reconnectCountdown <= 0f)
            {
                _reconnectCountdown = _reconnectDelaySec;
                _reconnectDelaySec = Mathf.Min(_reconnectDelaySec * 2f, MaxReconnectDelaySec);
                Reconnect();
            }
        }

        /// <summary>切断状態から利用者が再接続を選んだ場合。</summary>
        public void UserRequestedReconnect()
        {
            _reconnectDelaySec = 1f;
            _reconnectElapsed = 0f;
            SetState(ConnState.Joining);
            _pendingJoin = true;
            _pendingJoinLastSeq = _seqGuard.Expected - 1;
            NetBridge.Connect(_wsUrl);
        }

        private void HandleMessage(Dictionary<string, object> d)
        {
            string type = d.TryGetValue("type", out var t) ? t?.ToString() : null;
            switch (type)
            {
                case "__bridge_connected":
                    if (_pendingJoin)
                    {
                        SendJoin(_pendingJoinLastSeq);
                        _pendingJoin = false;
                    }
                    break;
                case "__bridge_disconnected":
                    if (State != ConnState.Lost && State != ConnState.Rejected)
                    {
                        SetState(ConnState.Reconnecting);
                        _reconnectCountdown = _reconnectDelaySec;
                    }
                    break;
                case "join_accepted":
                    HandleJoinAccepted(d);
                    break;
                case "refetch_required":
                    {
                        int from = (int)GetD(d, "from_seq");
                        int to = (int)GetD(d, "to_seq");
                        RefetchRequired?.Invoke(from, to);
                        SetState(_sendQueue.Pending.Count > 0 ? ConnState.Pending : ConnState.Synced);
                    }
                    break;
                case "fatal":
                    {
                        string reason = GetS(d, "reason");
                        SetState(reason == "board_lost" ? ConnState.Lost : ConnState.Rejected);
                        FatalReceived?.Invoke(reason);
                    }
                    break;
                case "active_delta":
                    HandleRemoteActiveDelta(d);
                    break;
                case "active_abort":
                    RemoteActiveAbort?.Invoke(GetS(d, "stroke_id"));
                    break;
                case "op_confirmed":
                    HandleOpConfirmed(d);
                    break;
                case "undo_flag_confirmed":
                    {
                        int seq = (int)GetD(d, "seq");
                        string opId = GetS(d, "op_id");
                        bool undone = d.TryGetValue("undone", out var u) && u is bool ub && ub;
                        UndoFlagConfirmed?.Invoke(opId, undone, seq);
                        _seqGuard.Accept(seq);
                    }
                    break;
                case "ack":
                    {
                        string opId = GetS(d, "op_id");
                        int seq = (int)GetD(d, "seq");
                        _sendQueue.Ack(opId, seq);
                        RefreshPendingSnapshot();
                        AckReceived?.Invoke(opId, seq);
                        if (_sendQueue.Pending.Count == 0 && (State == ConnState.Pending))
                        {
                            SetState(ConnState.Synced);
                        }
                    }
                    break;
                case "cursor":
                    {
                        string label = GetS(d, "label");
                        string color = GetS(d, "color");
                        float x = (float)GetD(d, "x");
                        float y = (float)GetD(d, "y");
                        RemoteCursor?.Invoke(label, new Vector2(x, y), color);
                    }
                    break;
                case "presence":
                    {
                        var joined = ToStringList(d, "joined");
                        var left = ToStringList(d, "left");
                        PresenceChanged?.Invoke(joined, left);
                    }
                    break;
            }
        }

        private void SendJoin(int lastSeq)
        {
            Send(new Dictionary<string, object>
            {
                ["type"] = "join",
                ["session_key"] = PendingSessionKey,
                ["board_token"] = PendingBoardToken,
                ["last_seq"] = (double)lastSeq,
            });
        }

        public string PendingSessionKey;
        public string PendingBoardToken;

        private void HandleJoinAccepted(Dictionary<string, object> d)
        {
            MyLabel = GetS(d, "label");
            MyColor = GetS(d, "color");
            int lastSeq = (int)GetD(d, "last_seq");
            _seqGuard.SetExpected(lastSeq + 1);
            _everConnectedOnce = true;
            _reconnectDelaySec = 1f;

            if (d.TryGetValue("catch_up_ops", out var opsObj) && opsObj is List<object> opsList)
            {
                foreach (var opObj in opsList)
                {
                    if (opObj is Dictionary<string, object> opDict)
                    {
                        var op = Op.FromDict(opDict);
                        if (_seqGuard.Accept(op.Seq))
                        {
                            OpConfirmed?.Invoke(op, op.Seq);
                        }
                    }
                }
            }

            // 保留中の未送信操作を同一op_idで再送する。
            foreach (var op in _sendQueue.ResendAll())
            {
                SendOpMessage(op);
            }
            RefreshPendingSnapshot();

            SetState(_sendQueue.Pending.Count > 0 ? ConnState.Pending : ConnState.Synced);
            JoinAccepted?.Invoke();
        }

        private void HandleRemoteActiveDelta(Dictionary<string, object> d)
        {
            string strokeId = GetS(d, "stroke_id");
            string color = GetS(d, "color");
            var tool = MasterData.ToolFromString(GetS(d, "tool"));
            var width = MasterData.WidthFromString(GetS(d, "width"));
            var points = JsonToPoints(d.TryGetValue("points", out var p) ? p as List<object> : null);
            RemoteActiveDelta?.Invoke(strokeId, points, color, tool, width);
        }

        private void HandleOpConfirmed(Dictionary<string, object> d)
        {
            var op = Op.FromDict(d);
            if (_seqGuard.Accept(op.Seq))
            {
                OpConfirmed?.Invoke(op, op.Seq);
            }
        }

        private static List<Vector2> JsonToPoints(List<object> arr)
        {
            var result = new List<Vector2>();
            if (arr == null)
            {
                return result;
            }
            foreach (var item in arr)
            {
                if (item is List<object> pair && pair.Count >= 2)
                {
                    float x = (float)Convert.ToDouble(pair[0]);
                    float y = (float)Convert.ToDouble(pair[1]);
                    result.Add(new Vector2(x, y));
                }
            }
            return result;
        }

        private static List<object> PointsToJson(List<Vector2> points)
        {
            var arr = new List<object>();
            if (points == null)
            {
                return arr;
            }
            foreach (var p in points)
            {
                arr.Add(new List<object> { (double)p.x, (double)p.y });
            }
            return arr;
        }

        private static List<string> ToStringList(Dictionary<string, object> d, string key)
        {
            var result = new List<string>();
            if (d.TryGetValue(key, out var v) && v is List<object> list)
            {
                foreach (var item in list)
                {
                    if (item != null)
                    {
                        result.Add(item.ToString());
                    }
                }
            }
            return result;
        }

        private static string GetS(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? v.ToString() : "";
        }

        private static double GetD(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : 0.0;
        }
    }
}
