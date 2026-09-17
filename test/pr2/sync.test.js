// test/pr2/sync.test.js
//
// PR #2（Issue #1 ワンショット実装）の結合E2Eテスト。
//
// PR本文のユーザーテスト手順は「Unity WebGL上で2つのブラウザタブを開いて
// 同時に描く・消す」という手順だが、Unity WebGLの実ブラウザ確認は
// tester役の範囲としては重すぎるため（jslibブリッジ経由の入力までは
// シミュレートしない）、ここでは Rails（アプリケーション層）+ Go中継の
// 結合動作――複数の疑似クライアントでの同時描画・消去・冪等性・追いつき
// ――を、実際のRailsサーバー・実際の中継サーバーを起動したうえで検証する。
// Unity側は demo-common-ui.test.js で「ビルド済みページの静的構造」のみ
// 軽量に確認する。
//
// 対象は requirements.md 27章の非機能要件（一貫性・冪等性・継続性）と、
// このタスクで指定された4つの必須シナリオ：
//   1. 2つの疑似クライアントが同時にボードへ参加し、片方のopがもう片方へ
//      op_confirmedとして配信されること
//   2. 同一op_idの再送で連番が重複採番されないこと
//   3. 消去済みストロークへの消去操作がエラーにならないこと
//   4. 参加時に古いlast_seqを渡すとバッファ範囲内なら追いつき配信、
//      範囲外ならrefetch_requiredが返ること

import { test, before, after } from "node:test";
import assert from "node:assert/strict";

import crypto from "node:crypto";

import { startRails, buildRelayBinary, startRelay } from "./lib/processes.js";
import { ApiClient } from "./lib/api-client.js";
import { WhiteboardSocket, sampleStroke } from "./lib/ws-client.js";

// テスト対象は永続化するRailsの開発用DB（daily resetまでデータが残る）。
// strokes.idはboard/session跨ぎのグローバルPKなので（db/schema.rb参照）、
// 固定文字列のIDを再利用すると前回のテスト実行分と衝突する
// （UNIQUE constraint failed: strokes.id）。実行ごとに一意な接頭辞を付ける。
const RUN_ID = crypto.randomUUID().slice(0, 8);
const id = (label) => `${RUN_ID}-${label}`;

let railsHandle;
let relayBinaryPath;
let relayHandle;

before(async () => {
  railsHandle = await startRails();
  relayBinaryPath = await buildRelayBinary();
  relayHandle = await startRelay(relayBinaryPath);
});

after(async () => {
  await relayHandle?.stop();
  await railsHandle?.stop();
});

/** セッション発行 + ボード作成を行い、作成者のAPIクライアントとボード情報を返す。 */
async function createBoardWithOwner(title) {
  const owner = new ApiClient();
  await owner.issueSession();
  const board = await owner.createBoard(title);
  return { owner, board };
}

/** 新しいセッションでボードに参加し、APIクライアントと参加情報を返す。 */
async function joinAsNewParticipant(boardToken) {
  const participant = new ApiClient();
  await participant.issueSession();
  const joinInfo = await participant.joinByToken(boardToken);
  return { participant, joinInfo };
}

/** WebSocketで接続してjoinメッセージを送り、join_acceptedを待つ。 */
async function connectAndJoin(sessionKey, boardToken, lastSeq = 0) {
  const socket = new WhiteboardSocket();
  await socket.connect();
  socket.join(sessionKey, boardToken, lastSeq);
  const accepted = await socket.waitForType("join_accepted");
  return { socket, accepted };
}

test("シナリオ1: 2クライアントが同時参加し、片方のopがもう片方へop_confirmedとして配信される", async () => {
  const { owner, board } = await createBoardWithOwner("シナリオ1: 同時参加と配信");
  const { participant } = await joinAsNewParticipant(board.board_token);

  const { socket: socketA } = await connectAndJoin(owner.sessionKey, board.board_token);
  const { socket: socketB } = await connectAndJoin(participant.sessionKey, board.board_token);

  try {
    const opId = id("s1-op-stroke-add");
    const stroke = sampleStroke(id("s1-stroke-1"));

    socketA.sendOp({ opId, kind: "stroke_add", stroke });

    // Aは受領応答（ack）のみを受け取る（自分のopはブロードキャスト対象外
    // ＝requirements.md 23.3節「送信と同時に自画面へ適用」の設計）。
    const ack = await socketA.waitForType("ack");
    assert.equal(ack.op_id, opId);
    assert.equal(ack.seq, 1);

    // Bはop_confirmedとして同じ操作を連番付きで受け取る。
    const confirmed = await socketB.waitForType("op_confirmed");
    assert.equal(confirmed.op_id, opId);
    assert.equal(confirmed.seq, 1);
    assert.equal(confirmed.kind, "stroke_add");
    assert.equal(confirmed.author_label, "参加者A");
    assert.equal(confirmed.stroke.id, stroke.id);
    assert.deepEqual(confirmed.stroke.points, stroke.points);

    // Aは自分が送った操作のop_confirmedを重ねて受け取らない
    // （Broadcastはexcept=送信者connIDで除外される）。
    await socketA.assertNoMessage((m) => m.type === "op_confirmed" && m.op_id === opId);
  } finally {
    socketA.close();
    socketB.close();
  }
});

test("シナリオ2: 同一op_idの再送で連番が重複採番されない（冪等性）", async () => {
  const { owner, board } = await createBoardWithOwner("シナリオ2: 冪等な再送");
  const { participant } = await joinAsNewParticipant(board.board_token);

  const { socket: socketA } = await connectAndJoin(owner.sessionKey, board.board_token);
  const { socket: socketB } = await connectAndJoin(participant.sessionKey, board.board_token);

  try {
    const opId = id("s2-op-stroke-add");
    const stroke = sampleStroke(id("s2-stroke-1"));

    socketA.sendOp({ opId, kind: "stroke_add", stroke });
    const ack1 = await socketA.waitForType("ack");
    assert.equal(ack1.seq, 1);

    const confirmed1 = await socketB.waitForType("op_confirmed");
    assert.equal(confirmed1.seq, 1);

    // 同じop_idで即座に再送（未送信キューの再送を模す。requirements.md 11.2節）。
    socketA.sendOp({ opId, kind: "stroke_add", stroke });
    const ack2 = await socketA.waitForType("ack");
    assert.equal(ack2.op_id, opId);
    assert.equal(ack2.seq, 1, "再送でも既に付与した連番がそのまま返ること");

    // 別のopを送って連番が2から続くことを確認する
    // （再送で内部の連番カウンタが余分に進んでいないことの検証）。
    const opId2 = id("s2-op-stroke-add-2");
    const stroke2 = sampleStroke(id("s2-stroke-2"), { color: "#E53935" });
    socketA.sendOp({ opId: opId2, kind: "stroke_add", stroke: stroke2 });
    const ack3 = await socketA.waitForType("ack");
    assert.equal(ack3.seq, 2, "再送はnextSeqを消費しないこと");

    // Bはop_confirmedを2件しか受け取らない（再送分のブロードキャストは無い）。
    const confirmedSeqs = [];
    confirmedSeqs.push((await socketB.waitForType("op_confirmed")).seq);
    await socketB.assertNoMessage((m) => m.type === "op_confirmed" && m.op_id === opId);
    assert.deepEqual(confirmedSeqs, [2]);

    // 永続化（200ms/50件で集約）を待ち、Rails側にも重複レコードが無いことを確認する。
    await waitForPersistedSeq(owner, board.board_id, 2);
    const persistedOps = await owner.fetchOps(board.board_id);
    const opIds = persistedOps.map((o) => o.op_id);
    assert.deepEqual(
      opIds.filter((id) => id === opId),
      [opId],
      "再送されたop_idがRails側に重複して永続化されていないこと"
    );
    assert.deepEqual(
      persistedOps.map((o) => o.seq),
      [1, 2],
      "連番が重複・欠番なく永続化されていること"
    );
  } finally {
    socketA.close();
    socketB.close();
  }
});

test("シナリオ3: 消去済みストロークへの消去操作はエラーにならない", async () => {
  const { owner, board } = await createBoardWithOwner("シナリオ3: 消去済みへの再消去");
  const { participant } = await joinAsNewParticipant(board.board_token);

  const { socket: socketA } = await connectAndJoin(owner.sessionKey, board.board_token);
  const { socket: socketB } = await connectAndJoin(participant.sessionKey, board.board_token);

  try {
    const strokeId = id("s3-stroke-1");
    const opAdd = id("s3-op-add");
    const opErase1 = id("s3-op-erase-1");
    const opErase2 = id("s3-op-erase-2");

    socketA.sendOp({ opId: opAdd, kind: "stroke_add", stroke: sampleStroke(strokeId) });
    await socketA.waitForType("ack");
    await socketB.waitForType("op_confirmed");

    // 1回目の消去（参加者A自身が消す）。
    socketA.sendOp({ opId: opErase1, kind: "stroke_erase", targetStrokeIds: [strokeId] });
    const ack1 = await socketA.waitForType("ack");
    assert.equal(ack1.op_id, opErase1);
    const confirmed1 = await socketB.waitForType("op_confirmed", { timeoutMs: 5000 });
    assert.equal(confirmed1.kind, "stroke_erase");

    // 2回目の消去（参加者Bが、既に消去済みの同じストロークを対象に送る）。
    // requirements.md 11.2節「消去の対象ストロークが既に消去済み…の場合、
    // 消去操作は無効な結果として適用され、エラーとしないこと」。
    socketB.sendOp({ opId: opErase2, kind: "stroke_erase", targetStrokeIds: [strokeId] });
    const ack2 = await socketB.waitForType("ack", { timeoutMs: 5000 });
    assert.equal(ack2.op_id, opErase2);
    assert.equal(typeof ack2.seq, "number");
    assert.ok(ack2.seq > confirmed1.seq, "エラーにせず新しい連番が採番されること");

    // fatalは送られず、接続も保持されたままであること。
    await socketA.assertNoMessage((m) => m.type === "fatal");
    await socketB.assertNoMessage((m) => m.type === "fatal");
    assert.equal(socketA.closed, false);
    assert.equal(socketB.closed, false);

    // Rails側にも2件目の消去操作が「エラーではなく記録済み」として残ること。
    await waitForPersistedSeq(owner, board.board_id, ack2.seq);
    const persistedOps = await owner.fetchOps(board.board_id);
    const secondErase = persistedOps.find((o) => o.op_id === opErase2);
    assert.ok(secondErase, "2回目の消去操作が操作ログに記録されていること");
    assert.deepEqual(secondErase.target_stroke_ids, [strokeId]);
  } finally {
    socketA.close();
    socketB.close();
  }
});

test("シナリオ4a: last_seqがバッファ範囲内なら参加受理と同時に追いつき配信される", async () => {
  const { owner, board } = await createBoardWithOwner("シナリオ4a: バッファ内の追いつき");

  const { socket: socketA } = await connectAndJoin(owner.sessionKey, board.board_token);
  try {
    socketA.sendOp({ opId: id("s4a-op-1"), kind: "stroke_add", stroke: sampleStroke(id("s4a-stroke-1")) });
    const ack1 = await socketA.waitForType("ack");
    assert.equal(ack1.seq, 1);

    socketA.sendOp({ opId: id("s4a-op-2"), kind: "stroke_add", stroke: sampleStroke(id("s4a-stroke-2")) });
    const ack2 = await socketA.waitForType("ack");
    assert.equal(ack2.seq, 2);

    // 同じ中継プロセスのハブがまだメモリ上に残っている間に、
    // last_seq=0（何も受信していない）で新規参加する。
    const { participant } = await joinAsNewParticipant(board.board_token);
    const { socket: socketB, accepted } = await connectAndJoin(participant.sessionKey, board.board_token, 0);
    try {
      assert.equal(accepted.last_seq, 2);
      assert.ok(Array.isArray(accepted.catch_up_ops), "catch_up_opsが配列で返ること");
      assert.deepEqual(
        accepted.catch_up_ops.map((o) => o.seq),
        [1, 2],
        "バッファに残っている確定操作が連番順に追いつき配信されること"
      );

      // 範囲外を示すrefetch_requiredは来ないこと。
      await socketB.assertNoMessage((m) => m.type === "refetch_required");
    } finally {
      socketB.close();
    }
  } finally {
    socketA.close();
  }
});

test("シナリオ4b: 中継サーバー再起動後、バッファ範囲外のlast_seqで参加するとrefetch_requiredが返る", async () => {
  const { owner, board } = await createBoardWithOwner("シナリオ4b: 再起動後の範囲外追いつき");

  const { socket: socketA } = await connectAndJoin(owner.sessionKey, board.board_token);
  socketA.sendOp({ opId: id("s4b-op-1"), kind: "stroke_add", stroke: sampleStroke(id("s4b-stroke-1")) });
  const ack1 = await socketA.waitForType("ack");
  assert.equal(ack1.seq, 1);
  socketA.sendOp({ opId: id("s4b-op-2"), kind: "stroke_add", stroke: sampleStroke(id("s4b-stroke-2")) });
  const ack2 = await socketA.waitForType("ack");
  assert.equal(ack2.seq, 2);
  socketA.close();

  // Railsへ確実に永続化されるのを待ってから中継を再起動する
  // （requirements.md 11.5節：再起動時はアプリケーション層の最新連番から
  // 採番を再開する。永続化前に再起動すると採番の起点がずれてしまう）。
  await waitForPersistedSeq(owner, board.board_id, 2);

  // 中継サーバーを再起動する。requirements.md 14表「中継の状態」：
  // ハブと直近バッファは揮発のため、再起動後の新しいハブは
  // このボードについて空のバッファを持つ（GetOrCreateがRailsのlast_seqから
  // 採番を再開するのみで、recentOpsは再構成されない）。
  await relayHandle.stop();
  relayHandle = await startRelay(relayBinaryPath);

  const { participant } = await joinAsNewParticipant(board.board_token);
  const { socket: socketB, accepted } = await connectAndJoin(participant.sessionKey, board.board_token, 0);
  try {
    assert.equal(accepted.last_seq, 2, "再起動後もRailsの連番から採番が再開されること");
    const refetch = await socketB.waitForType("refetch_required");
    assert.equal(refetch.from_seq, 0);
    assert.equal(refetch.to_seq, 2);

    // requirements.md 11.4節どおり、参加者はアプリケーション層から
    // 連番の範囲を指定して操作ログを取得する。
    const ops = await participant.fetchOps(board.board_id, { fromSeq: refetch.from_seq, toSeq: refetch.to_seq });
    assert.deepEqual(
      ops.map((o) => o.seq),
      [1, 2],
      "refetch_requiredで示された範囲をRailsから欠落なく取得できること"
    );
  } finally {
    socketB.close();
  }
});

/**
 * Go中継からRailsへの永続化書き込みは200ms/50件で集約されるため
 * （requirements.md 11.5節）、last_seqが目的の値に達するまで
 * GET /internal/boards/:id/last_seq 相当（ここではops一覧のseq最大値）で
 * ポーリングする。
 */
async function waitForPersistedSeq(apiClient, boardId, targetSeq, { timeoutMs = 15000 } = {}) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const ops = await apiClient.fetchOps(boardId);
    const maxSeq = ops.reduce((m, o) => Math.max(m, o.seq), 0);
    if (maxSeq >= targetSeq) return ops;
    if (Date.now() > deadline) {
      throw new Error(`timed out waiting for board ${boardId} to persist seq ${targetSeq} (have: ${maxSeq})`);
    }
    await new Promise((r) => setTimeout(r, 150));
  }
}
