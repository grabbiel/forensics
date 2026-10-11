# AI usage

## Tools and where they were used

- **Claude Code (Claude Opus):** research and part of the build (code, tests, migrations), browser checks of each journey, and suggestions for performance fixes.
- **Codex CLI and Claude sub-agents,** without write access: two independent reviews of each pull request through #26, with mutation checks of tests; findings were fixed before merging.
- **Cursor (agent):** the previous page (#33) and notifications (#34), from Claude Code's briefs.
- **A local Qwen model** (mlx-serve): early architecture proposals and critique.
- **Gemini 3.1 Pro (agy CLI):** rate-limiting proposals, cross-reviewed with Codex and Qwen.

Every change went through a pull request; from PR #7 on, the tests were required checks.

## A suggestion I accepted

Reviewing the transfer UI, Codex noted that a `5xx` can arrive after the API has committed a write. The UI treated it as a refusal and dropped the `Idempotency-Key`, so a retry would have been refused as a new write. I accepted it, since the API commits before it builds the response. A `5xx`, a lost answer or an `idempotency-in-flight` reply now counts as an unknown outcome: the key is kept, the evidence is reloaded, and the retry reuses the key. `transferActions.test.ts` and `TransferPanel.test.tsx` cover each case.

## A suggestion I rejected

During design, the local model proposed keeping idempotency keys in Redis, as faster and cheaper. That does not fit: the key must commit atomically with the transfer. With a separate store, a failure between the two writes either loses the key, so a retry repeats the write, or keeps a key for a write that never committed. Instead, the key and a SHA-256 fingerprint of the request are columns of the transfer row, written in the same transaction under a unique index per user. A retry gets the existing transfer back; another body with the same key gets `422`.

## What I reviewed most closely

How the API finds the caller's address behind Azure's front ends, since every rate limit keys on it. Trusting `X-Forwarded-For` too far lets a caller pick its own address, and so its own budget. Some AI-assisted reviews caught three ways in: the local model trusted the leftmost entry, which the caller writes; `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, if the platform set it, would add a trust-all pass; and the App Insights addresses I meant to check the rollout with were recorded before forwarding ran. The API now believes only configured ranges, one hop deep, refuses to start with that switch on or a `/0` range, and records the resolved address at response time.
