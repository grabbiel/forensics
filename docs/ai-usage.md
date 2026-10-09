# AI usage

## Tools and where they were used

- **Claude Code (Claude Opus):** research and the build (code, tests, migrations, these documents) and browser checks of each journey.
- **Codex CLI and Claude sub-agents,** without write access: two independent reviews of every pull request, including mutation checks of tests.
- **A local Qwen model** (mlx-serve): early architecture proposals and critique.

Every change went through a pull request, merged after its review findings were fixed; from PR #7 on, the tests were required checks.

## A suggestion I accepted

Reviewing the transfer UI, Codex noted that a `5xx` can arrive after the API has committed a write. The UI treated it as a refusal and dropped the `Idempotency-Key`, so a retry would have been refused as a new write. I accepted it, since the API commits before it builds the response. A `5xx`, a lost answer or an `idempotency-in-flight` reply now counts as an unknown outcome: the key is kept, the evidence is reloaded, and the retry reuses the key. `transferActions.test.ts` and `TransferPanel.test.tsx` cover each case.

## A suggestion I rejected

During design, the local model proposed keeping idempotency keys in Redis, as faster and cheaper. That does not fit: the key must commit atomically with the transfer. With a separate store, a failure between the two writes either loses the key, so a retry repeats the write, or keeps a key for a write that never committed. Instead, the key and a SHA-256 fingerprint of the request are columns of the transfer row, written in the same transaction under a unique index per user. A retry gets the existing transfer back; another body with the same key gets `422`.

## What I reviewed most closely

The custody write path in `CustodyTransferService`. It passed locally, but CI failed once on a race: one decision read the transfer before a concurrent commit and the evidence after it. After a step-by-step review, every write locks the evidence row first, then reads the transfer and checks for a replay, in one transaction. Parallel-duplicate and competing-accept tests run on every pull request. I gave the generated journey tests (`journeys.test.tsx`) the same scrutiny. The 409, reload, stale-response and modal tests stayed green under their first mutation checks, so I revised them until each regression made its test fail.
