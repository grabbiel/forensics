# AI usage

## Tools and where they were used

| Tool | Used for |
|---|---|
| Claude Code (Claude Opus) | Architecture research and the build: code, tests, migrations and these documents, plus a check of each journey in a browser |
| Codex CLI (read-only) | An independent review of every pull request and of the architecture options |
| Claude sub-agents (read-only) | A second review of every pull request, focused on accessibility and tests; it broke code in a throwaway copy to check that tests fail |
| A local Qwen model (mlx-serve) | Early architecture proposals and critique |

Every change went through a pull request with the backend and frontend tests as required checks. I merged each one after its review findings were fixed or answered.

## A suggestion I accepted

Reviewing the transfer UI, Codex pointed out that a `5xx` can arrive after the server has committed a write. The UI treated it as a refusal and dropped the `Idempotency-Key`. A retry would then have been a new write, refused with a `409` about the user's own action. I accepted it because the API does commit before it builds the response. A `5xx`, like a lost answer or an `idempotency-in-flight` reply, now counts as an unknown outcome: the key is kept, the page reads the evidence again, and the retry reuses the key. `transferActions.test.ts` and `TransferPanel.test.tsx` cover each case.

## A suggestion I rejected

During design, the local model proposed keeping idempotency keys in Redis, as faster and cheaper than SQL Server. It does not fit, because the key must commit atomically with the transfer. With a separate store, a failure between the two writes either loses the key, so a retry repeats the write, or keeps a key for a write that never committed. Instead, the key and a SHA-256 fingerprint of the request are columns of the transfer row, written in the same transaction under a unique index per user. A retry finds them and answers with the transfer it already made. The same key with a different body is refused with `422`.

## What I reviewed most closely

The custody write path in `CustodyTransferService`. The first version passed locally, but CI failed once on a race between two decisions: one read the transfer before a concurrent commit and the evidence after it. I reviewed the lock order step by step. Every write now locks the evidence row first, then reads the transfer and checks for a replay, all in one transaction. The concurrency tests (eight identical requests in parallel, competing accepts) run on every pull request.

I gave generated tests the same scrutiny: a test that passes proves little unless it fails when the behaviour regresses. The review of the end-to-end tests broke the code on purpose, and the 409, reload, stale-response and modal tests still passed. I rewrote them until each failed on its regression.
