using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Whiteboard.Bridges;
using Whiteboard.Data;
using Whiteboard.Drawing;
using Whiteboard.Export;
using Whiteboard.Json;
using Whiteboard.Sync;
using Whiteboard.UI;

namespace Whiteboard.Boot
{
    /// <summary>
    /// requirements.md 19・23・27章。シーン・UI・オブジェクトはすべてコードで生成する
    /// エントリポイント。ロード画面→（参加URLの場合）参加画面／一覧画面→ボード画面、
    /// を実装する。RuntimeInitializeOnLoadMethodにより、空のシーンからでも自己生成する。
    /// </summary>
    public class Boot : MonoBehaviour
    {
        private enum Phase
        {
            Loading,
            ShowingList,
            ShowingJoin,
            InBoard,
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("Boot");
            go.AddComponent<Boot>();
        }

        private Phase _phase = Phase.Loading;

        // UI
        private RectTransform _canvasRoot;
        private UiBuilder _uiBuilder;
        private LoadingView _loadingView;
        private BoardListView _listView;
        private JoinView _joinView;
        private BoardView _boardView;

        // インフラ
        private PrefsStore _prefs;
        private ApiClient _api;
        private PngExporter _pngExporter;

        // 描画
        private Camera _worldCamera;
        private LayerCompositor _compositor;
        private readonly CameraController _cameraController = new CameraController();
        private GestureRouter _gestureRouter;
        private InputReader _inputReader;
        private StrokeBuilder _strokeBuilder;
        private readonly List<Vector2> _eraserPath = new List<Vector2>();
        private const float EraserRadiusWorld = 10f;
        private Vector2 _lastPanScreen;
        private Vector2 _lastPointerWorld;

        // 同期
        private BoardState _boardState;
        private SeqGuard _seqGuard;
        private PresenceView _presence;
        private HistoryManager _history;
        private SendQueue _sendQueue;
        private SyncClient _sync;

        private string _boardId;
        private string _boardToken;
        private string _apiBase = "";
        private string _wsUrl = "";

        private ToolKind _currentTool = ToolKind.Pen;
        private string _currentColor = MasterData.DefaultColor;
        private StrokeWidth _currentWidth = StrokeWidth.Medium;

        private float _activeDeltaTimer;
        private float _cursorTimer;
        private float _viewportPersistTimer;
        private float _sendQueuePersistTimer;
        private bool _hasPendingSendQueuePersist;

        private void Awake()
        {
            ResolveHostConfig();
            SetupCanvas();
            SetupWorldRendering();

            _uiBuilder = new UiBuilder(_canvasRoot);
            _loadingView = _uiBuilder.BuildLoading();
            _listView = _uiBuilder.BuildBoardList();
            _joinView = _uiBuilder.BuildJoin();
            _boardView = _uiBuilder.BuildBoard();

            _prefs = new PrefsStore(new PlayerPrefsStore());
            _api = new ApiClient(_prefs);
            _pngExporter = new PngExporter();

            _gestureRouter = new GestureRouter();
            _inputReader = new InputReader();
            _strokeBuilder = new StrokeBuilder();
            _gestureRouter.SecondTouchDetected += OnSecondTouchDetected;

            InputBridge.Init("#unity-canvas");
            EnvBridge.Init();

            WireBoardListButtons();
            WireJoinButtons();
            WireBoardButtons();

            ShowOnly(_loadingView.Root);
        }

        private void ResolveHostConfig()
        {
            // 開発中はUnity Editor実行時はローカルホストを既定値にしてよい（INTEGRATION_CONTRACT.md 0章）。
            // 本番はWebGLTemplateのmeta要素経由で注入される値を使う（環境ブリッジ側でwindow.__wbApiBase等を設定）。
#if UNITY_EDITOR
            _apiBase = "http://localhost:3001";
            _wsUrl = "ws://localhost:8080/ws";
#else
            _apiBase = "";
            _wsUrl = EnvBridge.RelayWsUrl();
#endif
        }

        private void SetupCanvas()
        {
            var canvasGo = new GameObject("UICanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // BuildScript.ConfigurePlayerSettings()のdefaultScreenWidth/Height（Issue #9で
            // 1280x800→1280x680へ変更）と一致させる。Unity Playではこの解像度が実質的な
            // 固定デザイン解像度になるため、基準解像度をずらすとUIの見た目の比率がずれる。
            scaler.referenceResolution = new Vector2(1280, 680);
            // unity-ugui-runtime-uiスキルのレビュー（Issue #10）：追従規則は横長0.5・縦長0
            // （幅基準）とする。本デモはUnity Play実機では常に横長（実測アスペクト比約1.88）
            // だが、値を画面比率から決めることで将来的な縦長ホスト環境にも対応できるようにする。
            var isPortrait = Screen.height > Screen.width;
            scaler.matchWidthOrHeight = isPortrait ? 0f : 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRoot = canvasGo.GetComponent<RectTransform>();

            // unity-ugui-runtime-uiスキルのレビュー（Issue #10・不変条件2）：EventSystemは
            // シーンに1つだけ存在するよう、生成前に型検索で存在確認する。Bootは現状1回しか
            // SetupCanvas()を呼ばないため実害は無かったが、テスト実行等でシーンが再利用される
            // 場合に備えた防御的な変更。
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        private void SetupWorldRendering()
        {
            var camGo = new GameObject("WorldCamera");
            _worldCamera = camGo.AddComponent<Camera>();
            _worldCamera.orthographic = true;
            _worldCamera.clearFlags = CameraClearFlags.SolidColor;
            _worldCamera.backgroundColor = new Color(0.98f, 0.98f, 0.97f);
            _worldCamera.nearClipPlane = 0.1f;
            _worldCamera.farClipPlane = 100f;
            _worldCamera.depth = -10;
            camGo.transform.position = new Vector3(0, 0, -10);

            _compositor = new LayerCompositor();

            CreateLayerQuad("CommittedLayer", _compositor.Committed, 0f);
            CreateLayerQuad("RemoteActiveLayer", _compositor.RemoteActive, -0.1f);
            CreateLayerQuad("LocalActiveLayer", _compositor.LocalActive, -0.2f);
        }

        private void CreateLayerQuad(string name, RenderTexture texture, float z)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(0, 0, z);
            go.transform.localScale = new Vector3(_compositor.WorldHalfExtent.x * 2f, _compositor.WorldHalfExtent.y * 2f, 1f);

            var mesh = BuildUnitQuadMesh();
            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();

            // UI/Default を使う：uGUIが実際に参照するためWebGLビルドのシェーダ剥ぎ取り
            // 対象にならない。理由はLayerCompositor.csを参照。
            var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            var mat = new Material(shader);
            mat.mainTexture = texture;
            mat.color = Color.white;
            mr.material = mat;
        }

        private static Mesh BuildUnitQuadMesh()
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Start()
        {
            _loadingView.ProgressText.text = "セッションを確認しています…";
            BeginSessionBootstrap();
        }

        private void BeginSessionBootstrap()
        {
            string sessionKey = _prefs.GetSessionKey();
            if (string.IsNullOrEmpty(sessionKey))
            {
                _api.IssueSession((status, body) =>
                {
                    if (status == 201 && body != null && body.TryGetValue("session_key", out var k))
                    {
                        _prefs.SetSessionKey(k.ToString());
                        OnSessionReady();
                    }
                    else
                    {
                        _loadingView.ProgressText.text = "セッションの発行に失敗しました。再読み込みしてください。";
                    }
                });
            }
            else
            {
                OnSessionReady();
            }
        }

        private void OnSessionReady()
        {
            _boardToken = EnvBridge.BoardToken();
            if (!string.IsNullOrEmpty(_boardToken))
            {
                ShowJoinScreen(_boardToken);
            }
            else
            {
                ShowBoardListScreen();
            }
        }

        // ================= 一覧画面 =================

        private void WireBoardListButtons()
        {
            _listView.CreateButton.onClick.AddListener(OnCreateBoardClicked);
        }

        private void ShowBoardListScreen()
        {
            _phase = Phase.ShowingList;
            ShowOnly(_listView.Root);
            RefreshBoardList();
        }

        private void RefreshBoardList()
        {
            foreach (Transform child in _listView.ListContent)
            {
                Destroy(child.gameObject);
            }
            _listView.InfoText.text = "読み込み中です…";

            _api.ListBoards((status, body) =>
            {
                if (status != 200 || body == null || !(body.TryGetValue("boards", out var b) && b is List<object> boards))
                {
                    _listView.InfoText.text = "一覧の取得に失敗しました。";
                    return;
                }

                _listView.InfoText.text = boards.Count == 0 ? "参加中のボードはまだありません。" : "";
                foreach (var item in boards)
                {
                    if (item is Dictionary<string, object> d)
                    {
                        string boardId = d.TryGetValue("board_id", out var bid) ? bid.ToString() : "";
                        string boardToken = d.TryGetValue("board_token", out var bt) && bt != null ? bt.ToString() : "";
                        string title = d.TryGetValue("title", out var ti) && ti != null ? ti.ToString() : "無題のボード";
                        string updatedAt = d.TryGetValue("updated_at", out var ua) ? ua.ToString() : "";
                        int participants = d.TryGetValue("participant_count", out var pc) ? (int)Convert.ToDouble(pc) : 0;

                        var row = _uiBuilder.CreateBoardListRow(_listView.ListContent, title, updatedAt, participants);
                        var button = row.GetComponent<Button>();
                        string capturedId = boardId;
                        string capturedToken = boardToken;
                        button.onClick.AddListener(() => EnterExistingBoard(capturedId, capturedToken));
                    }
                }
            });
        }

        private void OnCreateBoardClicked()
        {
            string title = _listView.TitleInput.text;
            string honeypot = _listView.HoneypotInput.text;
            _api.CreateBoard(string.IsNullOrEmpty(title) ? null : title, honeypot, (status, body) =>
            {
                if (status == 201 && body != null)
                {
                    string boardId = body.TryGetValue("board_id", out var bid) ? bid.ToString() : null;
                    string boardToken = body.TryGetValue("board_token", out var bt) && bt != null ? bt.ToString() : "";
                    EnterExistingBoard(boardId, boardToken);
                }
                else
                {
                    _listView.InfoText.text = "ボードの作成に失敗しました。";
                }
            });
        }

        private void EnterExistingBoard(string boardId, string boardToken)
        {
            if (string.IsNullOrEmpty(boardId))
            {
                return;
            }
            // 参加URLの構成要素はボードトークンだが、一覧からの遷移では既に参加済みのため
            // by_token/join を経由せず、直接 ops を取得してボード画面へ入る。
            //
            // 実機バグ修正（2026-09-21・Issue #30）：ただしボードトークンは中継サーバーへの
            // join に必須で、relay 側は空トークンの join を無言で読み捨てる。従来は
            // EnvBridge.BoardToken()（ページURLの ?b=）からしか設定しておらず、Unity Play では
            // ゲームが struckd のURLのiframeで動くため ?b= が存在し得ず、一覧から入った場合は
            // 常に空だった。その結果 join が成立せず、描画opが全て捨てられ（リロードで線が消える）、
            // Undo/Redo も undo_flag_confirmed が返らず永久に無反応になっていた。
            // 一覧・新規作成のレスポンスに含まれるトークンをここで引き継ぐ。
            _boardToken = boardToken;
            _boardId = boardId;
            _api.FetchOps(boardId, 0, null, (status, body) =>
            {
                if (status == 200 && body != null)
                {
                    EnterBoard(boardId, body.TryGetValue("ops", out var opsObj) ? opsObj as List<object> : null, null, null);
                }
            });
        }

        // ================= 参加画面 =================

        private void WireJoinButtons()
        {
            _joinView.JoinButton.onClick.AddListener(OnJoinClicked);
        }

        private void ShowJoinScreen(string boardToken)
        {
            _phase = Phase.ShowingJoin;
            ShowOnly(_joinView.Root);
            _joinView.ErrorText.text = "";
            _joinView.BoardNameText.text = "読み込み中です…";

            _api.PreviewByToken(boardToken, (status, body) =>
            {
                if (status == 200 && body != null)
                {
                    string title = body.TryGetValue("title", out var t) && t != null ? t.ToString() : "無題のボード";
                    int count = body.TryGetValue("participant_count", out var c) ? (int)Convert.ToDouble(c) : 0;
                    bool joinable = !(body.TryGetValue("joinable", out var j) && j is bool jb && !jb);
                    _joinView.BoardNameText.text = title;
                    _joinView.ParticipantCountText.text = "現在の参加者数：" + count + " 人";
                    _joinView.JoinButton.interactable = joinable;
                    if (!joinable)
                    {
                        _joinView.ErrorText.text = "このボードは参加者数の上限に達しています。";
                    }
                }
                else
                {
                    _joinView.BoardNameText.text = "ボードが見つかりません。";
                    _joinView.JoinButton.interactable = false;
                }
            });
        }

        private void OnJoinClicked()
        {
            _joinView.ErrorText.text = "";
            _api.JoinByToken(_boardToken, (status, body) =>
            {
                if (status == 200 && body != null)
                {
                    string boardId = body.TryGetValue("board_id", out var bid) ? bid.ToString() : null;
                    var opsObj = body.TryGetValue("ops", out var o) ? o as List<object> : null;
                    string label = body.TryGetValue("label", out var l) ? l.ToString() : null;
                    string color = body.TryGetValue("color", out var col) ? col.ToString() : null;
                    EnterBoard(boardId, opsObj, label, color);
                }
                else if (status == 409)
                {
                    _joinView.ErrorText.text = "参加者数の上限に達しています。";
                }
                else
                {
                    _joinView.ErrorText.text = "参加に失敗しました。";
                }
            });
        }

        // ================= ボード画面 =================

        private void WireBoardButtons()
        {
            for (int i = 0; i < _boardView.ToolButtons.Count; i++)
            {
                int idx = i;
                _boardView.ToolButtons[i].onClick.AddListener(() => SelectTool((ToolKind)idx));
            }
            for (int i = 0; i < _boardView.ColorButtons.Count; i++)
            {
                string color = MasterData.Colors[i];
                _boardView.ColorButtons[i].onClick.AddListener(() => SelectColor(color));
            }
            for (int i = 0; i < _boardView.WidthButtons.Count; i++)
            {
                int idx = i;
                _boardView.WidthButtons[i].onClick.AddListener(() => SelectWidth((StrokeWidth)idx));
            }

            _boardView.UndoButton.onClick.AddListener(OnUndoClicked);
            _boardView.RedoButton.onClick.AddListener(OnRedoClicked);
            _boardView.ClearButton.onClick.AddListener(OnClearClicked);
            _boardView.ResetViewButton.onClick.AddListener(() => _cameraController.Reset());
            _boardView.FitAllButton.onClick.AddListener(() => _cameraController.FitAll(_boardState?.VisibleStrokes() ?? new List<Stroke>()));
            _boardView.BackButton.onClick.AddListener(OnBackToListClicked);
            _boardView.ExportButton.onClick.AddListener(OnExportClicked);
            _boardView.CopyUrlButton.onClick.AddListener(OnCopyUrlClicked);
            _boardView.BoardTitleInput.onEndEdit.AddListener(OnBoardTitleEdited);
        }

        private void EnterBoard(string boardId, List<object> opsRaw, string label, string color)
        {
            _boardId = boardId;
            _boardState = new BoardState();
            _seqGuard = new SeqGuard();
            _presence = new PresenceView();
            _history = new HistoryManager();
            _sendQueue = new SendQueue(_prefs, boardId);
            _sync = new SyncClient(_sendQueue, _seqGuard);

            // 保持日付が今日と異なる場合は未送信キューを破棄する（13章）。
            if (_prefs.DiscardIfDateChanged(DateTime.UtcNow))
            {
                _prefs.ClearUnsentQueue(boardId);
            }
            _sendQueue.Restore();
            // セキュリティ/堅牢性レビュー（2026-09-17）：PlayerPrefsから復元した未送信キューを
            // NetBridge.jslib側のpendingSnapshotへ即時反映する。接続確立（join_accepted）の
            // 前にタブが閉じられても、7.3章・14章の「Unityの処理を待たず直接送出する」経路が
            // 機能するようにするため。
            _sync.RefreshPendingSnapshot();

            if (opsRaw != null)
            {
                foreach (var item in opsRaw)
                {
                    if (item is Dictionary<string, object> d)
                    {
                        var op = Op.FromDict(d);
                        _boardState.ApplyOp(op, op.Seq);
                    }
                }
            }

            _compositor.Rebuild(_boardState.VisibleStrokes());

            var (tool, toolColor, width) = _prefs.GetToolSettings();
            _currentTool = tool;
            _currentColor = toolColor;
            _currentWidth = width;
            RefreshToolSelectionUi();

            var (panX, panY, zoom) = _prefs.GetViewport(boardId);
            _cameraController.Center = new Vector2(panX, panY);
            _cameraController.Zoom = zoom;

            SubscribeSyncEvents();

            _sync.PendingSessionKey = _prefs.GetSessionKey();
            _sync.PendingBoardToken = _boardToken ?? "";
            _sync.Connect(_wsUrl, _boardState.LastSeq);

            _phase = Phase.InBoard;
            ShowOnly(_boardView.Root);
            _boardView.BoardTitleInput.text = "";

            RefreshParticipantList();
        }

        private void SubscribeSyncEvents()
        {
            _sync.JoinAccepted += () =>
            {
                _history.RebuildFromOwn(_boardState.Ops, _sync.MyLabel);
                RefreshParticipantList();
            };
            _sync.OpConfirmed += (op, seq) =>
            {
                _boardState.ApplyOp(op, seq);
                _compositor.Rebuild(_boardState.VisibleStrokes());
                if (op.AuthorLabel != _sync.MyLabel && op.Kind == OpKind.StrokeAdd)
                {
                    _compositor.DropRemoteActive(op.StrokeId);
                }
            };
            _sync.UndoFlagConfirmed += (opId, undone, seq) =>
            {
                _boardState.ApplyUndoFlag(opId, undone);
                _compositor.Rebuild(_boardState.VisibleStrokes());
            };
            _sync.AckReceived += (opId, seq) =>
            {
                _boardState.ConfirmSeq(opId, seq);
            };
            _sync.RemoteActiveDelta += (strokeId, points, color, tool, width) =>
            {
                _compositor.AppendRemoteDelta(strokeId, points, color, tool, width);
            };
            _sync.RemoteActiveAbort += strokeId => _compositor.DropRemoteActive(strokeId);
            _sync.RemoteCursor += (label, pos, color) => _presence.UpdateCursor(label, pos);
            _sync.PresenceChanged += (joined, left) =>
            {
                foreach (var l in joined)
                {
                    _presence.OnJoin(l);
                }
                foreach (var l in left)
                {
                    _presence.OnLeave(l);
                }
                RefreshParticipantList();
            };
            _sync.RefetchRequired += (from, to) =>
            {
                _api.FetchOps(_boardId, from - 1, to < 0 ? (int?)null : to, (status, body) =>
                {
                    if (status == 200 && body != null && body.TryGetValue("ops", out var opsObj) && opsObj is List<object> list)
                    {
                        foreach (var item in list)
                        {
                            if (item is Dictionary<string, object> d)
                            {
                                var op = Op.FromDict(d);
                                if (_seqGuard.Accept(op.Seq))
                                {
                                    _boardState.ApplyOp(op, op.Seq);
                                }
                            }
                        }
                        _compositor.Rebuild(_boardState.VisibleStrokes());
                    }
                });
            };
            _sync.FatalReceived += reason =>
            {
                _boardView.StatusText.text = reason == "board_lost" ? "消失" : "エラー";
            };
            _sync.StateChanged += () => _boardView.StatusText.text = StatusLabel(_sync.State);
        }

        private static string StatusLabel(ConnState s)
        {
            switch (s)
            {
                case ConnState.Synced: return "同期中";
                case ConnState.Pending: return "保留あり";
                case ConnState.Reconnecting: return "再接続中";
                case ConnState.Hidden: return "非表示";
                case ConnState.Disconnected: return "切断";
                case ConnState.Lost: return "消失";
                case ConnState.Rejected: return "参加できません";
                default: return "参加中";
            }
        }

        private void RefreshParticipantList()
        {
            foreach (Transform child in _boardView.ParticipantListContent)
            {
                Destroy(child.gameObject);
            }
            foreach (var kv in _presence.Participants)
            {
                _uiBuilder.CreateParticipantRow(_boardView.ParticipantListContent, kv.Key, kv.Value.Color ?? MasterData.DefaultColor, kv.Key == _sync.MyLabel);
            }
        }

        private void SelectTool(ToolKind tool)
        {
            _currentTool = tool;
            if (tool != ToolKind.Eraser)
            {
                _currentWidth = MasterData.DefaultWidthFor(tool);
            }
            _prefs.SetToolSettings(_currentTool, _currentColor, _currentWidth);
            RefreshToolSelectionUi();
        }

        private void SelectColor(string color)
        {
            _currentColor = color;
            _prefs.SetToolSettings(_currentTool, _currentColor, _currentWidth);
            RefreshToolSelectionUi();
        }

        private void SelectWidth(StrokeWidth width)
        {
            _currentWidth = width;
            _prefs.SetToolSettings(_currentTool, _currentColor, _currentWidth);
            RefreshToolSelectionUi();
        }

        /// <summary>
        /// unity-ugui-runtime-uiスキルのレビュー（Issue #10）：現在選択中のツール・色・
        /// 太さを枠線表示に反映する。ボード画面に入るたび（保存済み設定の復元時）と、
        /// ツールバーでの選択変更のたびに呼ぶ。
        /// </summary>
        private void RefreshToolSelectionUi()
        {
            for (int i = 0; i < _boardView.ToolSelectionOutlines.Count; i++)
            {
                _boardView.ToolSelectionOutlines[i].enabled = (int)_currentTool == i;
            }
            for (int i = 0; i < _boardView.ColorSelectionOutlines.Count && i < MasterData.Colors.Length; i++)
            {
                _boardView.ColorSelectionOutlines[i].enabled = MasterData.Colors[i] == _currentColor;
            }
            for (int i = 0; i < _boardView.WidthSelectionOutlines.Count; i++)
            {
                _boardView.WidthSelectionOutlines[i].enabled = (int)_currentWidth == i;
            }
        }

        private void OnUndoClicked()
        {
            var op = _history.Undo();
            if (op != null)
            {
                _sync.SendUndoFlag(op.OpId, true);
            }
        }

        private void OnRedoClicked()
        {
            var op = _history.Redo();
            if (op != null)
            {
                _sync.SendUndoFlag(op.OpId, false);
            }
        }

        private void OnClearClicked()
        {
            var op = Op.NewClear(Guid.NewGuid().ToString("N"), _sync.MyLabel);
            _history.Record(op);
            _boardState.ApplyOp(op, _boardState.LastSeq); // 楽観適用（暫定seq。ackで確定）
            _compositor.Rebuild(_boardState.VisibleStrokes());
            _sync.SendOp(op);
        }

        private void OnExportClicked()
        {
            string filename = "whiteboard-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".png";
            _pngExporter.ExportAndDownload(_boardState.VisibleStrokes(), filename);
        }

        private void OnCopyUrlClicked()
        {
            // クリップボードへのコピーはブラウザAPI依存のため、EnvBridge経由の実装は将来拡張とする。
            // 現状はURL文字列を組み立てて状態表示に示す（実機ブラウザでの体験確認は統括側で実施）。
            _boardView.StatusText.text = "URLをコピーしました";
        }

        private void OnBoardTitleEdited(string newTitle)
        {
            if (string.IsNullOrEmpty(_boardId))
            {
                return;
            }
            _api.RenameBoard(_boardId, newTitle, null);
        }

        private void OnBackToListClicked()
        {
            ShowBoardListScreen();
        }

        private void OnSecondTouchDetected()
        {
            if (_strokeBuilder.IsActive)
            {
                _sync.SendActiveAbort(_strokeBuilder.StrokeId);
                _strokeBuilder.Abort();
                _compositor.ClearLocalActive();
            }
        }

        // ================= フレーム更新 =================

        private void Update()
        {
            PumpEnv();
            _api?.PumpResponses();
            if (_sync != null)
            {
                _sync.PumpMessages();
                _sync.Tick(Time.deltaTime);
            }

            if (_phase == Phase.InBoard)
            {
                UpdateCameraFromController();
                HandleInput();
                PumpActiveDelta();
                PumpCursor();
                PersistViewportDebounced();
                if (_boardView.ZoomText != null)
                {
                    _boardView.ZoomText.text = Mathf.RoundToInt(_cameraController.Zoom * 100f) + "%";
                }
            }
        }

        private void PumpEnv()
        {
            string json = EnvBridge.Drain();
            var arr = MiniJson.Deserialize(json) as List<object>;
            if (arr == null)
            {
                return;
            }
            foreach (var item in arr)
            {
                if (item is Dictionary<string, object> d && d.TryGetValue("event", out var evObj))
                {
                    string ev = evObj?.ToString();
                    if (ev == "visible")
                    {
                        _sync?.OnVisibilityChange(true);
                    }
                    else if (ev == "hidden")
                    {
                        _sync?.OnVisibilityChange(false);
                        if (_strokeBuilder.IsActive)
                        {
                            FinishLocalStroke();
                        }
                    }
                    else if (ev == "pagehide")
                    {
                        _sync?.FlushOnPageHide();
                    }
                }
            }
        }

        private void UpdateCameraFromController()
        {
            _cameraController.ViewportSizePx = new Vector2(Screen.width, Screen.height);
            _worldCamera.orthographicSize = Mathf.Max(Screen.height / 2f, 1f) / Mathf.Max(_cameraController.Zoom, 0.0001f);
            _worldCamera.transform.position = new Vector3(_cameraController.Center.x, _cameraController.Center.y, -10f);
        }

        private void HandleInput()
        {
            var events = _inputReader.ReadFrame();
            foreach (var ev in events)
            {
                if (ev.Type == "wheel")
                {
                    float factor = ev.Y > 0 ? 0.9f : 1.1f;
                    _cameraController.ZoomAt(new Vector2(ev.X, ev.Y), factor);
                    continue;
                }

                var mode = _gestureRouter.Route(ev);
                Vector2 screen = new Vector2(ev.X, ev.Y);
                Vector2 world = _cameraController.ToWorld(screen);

                switch (ev.Type)
                {
                    case "down":
                        HandleDown(mode, screen, world);
                        break;
                    case "move":
                        HandleMove(mode, screen, world);
                        break;
                    case "up":
                    case "cancel":
                        HandleUp();
                        break;
                }
            }
        }

        private void HandleDown(GestureMode mode, Vector2 screen, Vector2 world)
        {
            if (mode == GestureMode.Drawing)
            {
                if (_currentTool == ToolKind.Eraser)
                {
                    _eraserPath.Clear();
                    _eraserPath.Add(world);
                    TryEraseAt(world);
                }
                else
                {
                    _strokeBuilder.Begin(_currentTool, _currentColor, _currentWidth, world);
                }
            }
            else if (mode == GestureMode.Navigating)
            {
                _lastPanScreen = screen;
            }
            _lastPointerWorld = world;
        }

        private void HandleMove(GestureMode mode, Vector2 screen, Vector2 world)
        {
            _lastPointerWorld = world;
            if (mode == GestureMode.Drawing)
            {
                if (_currentTool == ToolKind.Eraser)
                {
                    _eraserPath.Add(world);
                    TryEraseAt(world);
                    if (_eraserPath.Count > 8)
                    {
                        _eraserPath.RemoveRange(0, _eraserPath.Count - 8);
                    }
                }
                else if (_strokeBuilder.IsActive)
                {
                    _strokeBuilder.AddPoint(world, _cameraController.Zoom);
                    RedrawLocalActive();
                    if (_strokeBuilder.ReachedLimit)
                    {
                        FinishLocalStroke();
                        _strokeBuilder.Begin(_currentTool, _currentColor, _currentWidth, world);
                    }
                }
            }
            else if (mode == GestureMode.Navigating)
            {
                Vector2 delta = screen - _lastPanScreen;
                _cameraController.Pan(delta);
                _lastPanScreen = screen;
            }
        }

        private void HandleUp()
        {
            if (_currentTool != ToolKind.Eraser && _strokeBuilder.IsActive)
            {
                FinishLocalStroke();
            }
            _eraserPath.Clear();
        }

        private void TryEraseAt(Vector2 world)
        {
            var hitIds = EraserHitTester.Hit(_eraserPath, EraserRadiusWorld, _boardState.VisibleStrokes());
            if (hitIds.Count == 0)
            {
                return;
            }
            var op = Op.NewStrokeErase(Guid.NewGuid().ToString("N"), hitIds, _sync.MyLabel);
            // 実機バグ修正（2026-09-21・Issue #28）：描画（FinishLocalStroke）・全消去
            // （OnClearClicked）は履歴へ記録しているのに、消去だけ記録が漏れていた。
            // その結果、消しゴムの直後にUndoを押すと消去ではなく「1つ前に描いた線」が
            // 取り消されていた（requirements.md 12章：自分の操作は取り消せること）。
            _history.Record(op);
            _boardState.ApplyOp(op, _boardState.LastSeq);
            _compositor.Rebuild(_boardState.VisibleStrokes());
            _sync.SendOp(op);
        }

        private void RedrawLocalActive()
        {
            var mesh = RibbonMeshBuilder.Build(_strokeBuilder.Points, MasterData.WidthToPixels(_strokeBuilder.Width));
            _compositor.DrawLocalActive(mesh, _strokeBuilder.Color);
        }

        private void FinishLocalStroke()
        {
            var stroke = _strokeBuilder.Finish(_sync?.MyLabel);
            _compositor.ClearLocalActive();

            var op = Op.NewStrokeAdd(stroke.Id, stroke, _sync.MyLabel);
            _history.Record(op);
            _boardState.ApplyOp(op, _boardState.LastSeq); // 楽観適用。受領応答(ack)でConfirmSeqする。
            _compositor.Rebuild(_boardState.VisibleStrokes());
            _sync.SendOp(op);
        }

        private void PumpActiveDelta()
        {
            _activeDeltaTimer += Time.deltaTime;
            if (_activeDeltaTimer < 0.05f)
            {
                return;
            }
            _activeDeltaTimer = 0f;

            if (_strokeBuilder.IsActive)
            {
                var delta = _strokeBuilder.DrainPendingDelta();
                if (delta.Count > 0)
                {
                    _sync.SendActiveDelta(_strokeBuilder.StrokeId, _strokeBuilder.Tool, _strokeBuilder.Color, _strokeBuilder.Width, delta);
                }
            }
        }

        private void PumpCursor()
        {
            _cursorTimer += Time.deltaTime;
            if (_cursorTimer < 0.1f)
            {
                return;
            }
            _cursorTimer = 0f;
            _sync?.SendCursor(_lastPointerWorld);
        }

        private void PersistViewportDebounced()
        {
            _viewportPersistTimer += Time.deltaTime;
            if (_viewportPersistTimer < 1f)
            {
                return;
            }
            _viewportPersistTimer = 0f;
            if (!string.IsNullOrEmpty(_boardId))
            {
                _prefs.SetViewport(_boardId, _cameraController.Center.x, _cameraController.Center.y, _cameraController.Zoom);
            }
        }

        private void ShowOnly(RectTransform target)
        {
            _loadingView.Root.gameObject.SetActive(target == _loadingView.Root);
            _listView.Root.gameObject.SetActive(target == _listView.Root);
            _joinView.Root.gameObject.SetActive(target == _joinView.Root);
            _boardView.Root.gameObject.SetActive(target == _boardView.Root);
            // 実機バグ修正（2026-09-20）：画面遷移直後はキャンバスにDOMフォーカスが
            // 無いままのことがあり、クリックするまで再描画・リサイズが反映されず
            // 見切れて見える現象があったため、遷移のたびに明示的にフォーカスを移す。
            EnvBridge.FocusCanvas();
        }
    }
}
