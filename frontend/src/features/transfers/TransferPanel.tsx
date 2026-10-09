import { ArrowClockwise, CheckCircle, Hourglass, PaperPlaneTilt, Question, WarningOctagon, XCircle } from '@phosphor-icons/react'
import { useEffect, useEffectEvent, useRef, useState, type ReactNode, type RefObject } from 'react'
import { useFetcher } from 'react-router'
import type { ProblemDetails } from '../../api/client'
import type { ChainEvent, EvidenceDetail, PersonRef, TransferStatus, TransferView } from '../../api/evidence'
import type { Person } from '../../api/people'
import type { TransferResource } from '../../api/transfers'
import type { SessionUser } from '../../auth/session'
import { formatUtcDateTime } from '../../lib/format'
import { useWaited } from '../../lib/useWaited'
import { clearIntent, getIntent, intentScope, startIntent, type IntentKind, type PendingIntent } from './pendingIntent'
import { RejectDialog, RequestDialog, type RequestFields } from './TransferDialogs'
import { PROBLEM, type WriteResult } from './transferActions'

// Event times are the server's and intent times this browser's; a request seen this much before it was sent is older.
const CLOCK_SKEW_MS = 5 * 60_000

/** What the last write came to, kept until the next one: the page re-reads itself, this stays. */
interface Notice {
  kind: 'success' | 'conflict' | 'unknown' | 'error'
  text: string
  /** After a 409: the transfer the user acted on, as the server says it is now. */
  state?: { transfer: TransferView; status: TransferStatus; actedBy?: PersonRef }
  /** The write a retry resends with the same key, or that can be discarded. */
  retry?: IntentKind
  /** After a 429: the seconds before the retry can go. */
  wait?: number
  /** True when it answers something the user just did; a notice restored on load does not take focus. */
  focus: boolean
}

type Unanswered = { status: number; problem?: ProblemDetails }

/**
 * Journey 2 on the evidence page: the pending transfer with the recipient's decision, or the request for a new one.
 * The fetchers live here, keyed by evidence, so they outlive the dialogs and the page's own re-reads. Mount it keyed
 * by evidence, so a notice never carries over to another.
 */
export function TransferPanel({
  detail,
  chain,
  custodians,
  user,
  headingRef,
}: {
  detail: EvidenceDetail
  chain: ChainEvent[]
  custodians: Person[]
  user: SessionUser
  headingRef: RefObject<HTMLElement | null>
}) {
  const code = detail.code
  const scopes = { request: intentScope('request', user.id, code), decision: intentScope('decision', user.id, code) }
  const pending = detail.pendingTransfer
  const isRecipient = pending !== null && user.role === 'Custodio' && pending.to.id === user.id
  const canRequest = pending === null && user.role === 'Investigador'
  const nameOf = (id: number) => custodians.find((c) => c.id === id)?.displayName ?? `#${id}`

  const loaded: Loaded = { scopes, chain, user, pending }

  const request = useFetcher<WriteResult>({ key: `request:${code}` })
  const decision = useFetcher<WriteResult>({ key: `decision:${code}` })
  // A write left without a known outcome (a reload, or the page left while it ran) is checked against this load.
  const [notice, setNotice] = useState<Notice | null>(
    () =>
      (request.state === 'idle' && !request.data ? reconcileRequest(loaded, undefined, false) : null) ??
      (decision.state === 'idle' && !decision.data ? reconcileDecision(loaded, undefined, false) : null),
  )
  // After a 429, a retry before the server's wait is over would only be turned away again.
  const waited = useWaited(notice?.wait, notice)
  const noticeRef = useRef<HTMLDivElement>(null)
  const titleRef = useRef<HTMLHeadingElement>(null)
  // The transfer a decision was sent for: once a 409 says it was decided, the page no longer loads it.
  const decidedOn = useRef<TransferView | null>(null)

  function send(fetcher: typeof request, intent: PendingIntent) {
    const { fields } = intent
    const action = fields.decision ? `/transfers/${fields.transferId}/${fields.decision}` : `/evidence/${encodeURIComponent(code)}/transfer`
    setNotice(null)
    fetcher.submit({ ...fields, idempotencyKey: intent.idempotencyKey }, { method: 'post', action })
  }

  function requestTransfer(fields: RequestFields) {
    send(request, startIntent(scopes.request, { ...fields }))
  }

  function decide(kind: 'accept' | 'reject', transfer: TransferView, text = '') {
    decidedOn.current = transfer
    const fields = { decision: kind, transferId: String(transfer.transferId), etag: transfer.etag, evidenceCode: code, [kind === 'accept' ? 'notes' : 'reason']: text }
    send(decision, startIntent(scopes.decision, fields))
  }

  // The notice and its buttons go away, so focus waits on the section title for the answer.
  function retry(which: IntentKind) {
    titleRef.current?.focus()
    const intent = getIntent(scopes[which])
    if (intent) send(which === 'request' ? request : decision, intent)
    else setNotice(null)
  }

  function discard(which: IntentKind) {
    titleRef.current?.focus()
    clearIntent(scopes[which])
    setNotice(null)
  }

  // Each answer becomes a notice once the page has re-read itself (the fetcher is idle), and moves focus there.
  useSettled(request, (result) => {
    if (result.outcome === 'unknown') return setNotice(reconcileRequest(loaded, result, true))
    if (result.outcome === 'throttled') return setNotice(throttled(result))
    if (result.outcome === 'done') return setNotice({ kind: 'success', focus: true, text: requestDone(result.transfer, result.replayed) })
    setNotice(refusal(result, user, nameOf, decidedOn.current))
  })

  useSettled(decision, (result) => {
    if (result.outcome === 'unknown') return setNotice(reconcileDecision(loaded, result, true))
    if (result.outcome === 'throttled') return setNotice(throttled(result))
    if (result.outcome === 'done') {
      const text =
        result.transfer.status === 'Accepted'
          ? `Aceptaste la custodia de ${code}.`
          : `Rechazaste la transferencia; la custodia sigue con ${result.transfer.from.displayName}.`
      return setNotice({ kind: 'success', focus: true, text })
    }
    setNotice(refusal(result, user, nameOf, decidedOn.current))
  })

  // A notice that answers the user takes focus, so it is read and the user is not left on a button that may have gone.
  useEffect(() => {
    if (notice?.focus) noticeRef.current?.focus()
  }, [notice])

  const sendingRequest = request.state !== 'idle' ? (request.formData ?? undefined) : undefined
  const deciding = decision.state !== 'idle' ? String(decision.formData?.get('decision') ?? 'accept') : undefined
  const checking = (request.state === 'loading' && request.data?.outcome === 'unknown') || (decision.state === 'loading' && decision.data?.outcome === 'unknown')
  const requestIntent = getIntent(scopes.request)
  const decided = notice?.state
  const shown = pending ?? decided?.transfer ?? null
  const shownDecided = decided && shown && decided.transfer.transferId === shown.transferId ? decided : undefined
  const progress = checking
    ? 'Resultado desconocido: comprobando si llegó…'
    : deciding
      ? `Enviando ${deciding === 'reject' ? 'el rechazo' : 'la aceptación'}…`
      : sendingRequest
        ? 'Enviando la solicitud…'
        : ''

  return (
    <section className="transfers" aria-labelledby="transfers-title">
      <h2 id="transfers-title" className="section-title" ref={titleRef} tabIndex={-1}>
        Transferencia de custodia
      </h2>
      {/* Always present, so each step is announced as it starts. */}
      <p className="visually-hidden" role="status">
        {progress}
      </p>

      {notice && (
        <div ref={noticeRef} tabIndex={-1} role={notice.kind === 'success' ? 'status' : 'alert'} className={`notice notice--${notice.kind}`}>
          <NoticeIcon kind={notice.kind} />
          <p>
            {notice.text}
            {notice.wait !== undefined && (waited ? ' Ya puedes reintentar.' : ` Espera ${notice.wait} s antes de reintentar.`)}
          </p>
          {notice.retry && (
            <div className="notice__actions">
              {/* aria-disabled, not disabled, so focus can rest on it while the wait runs. */}
              <button type="button" className="button button--ghost" aria-disabled={!waited} onClick={() => waited && retry(notice.retry!)}>
                <ArrowClockwise size={16} aria-hidden="true" />
                Reintentar
              </button>
              <button type="button" className="button button--ghost" onClick={() => discard(notice.retry!)}>
                Descartar
              </button>
            </div>
          )}
        </div>
      )}

      {checking && (
        <p className="transfer transfer--checking" aria-hidden="true">
          <Question size={18} aria-hidden="true" />
          Resultado desconocido – comprobando…
        </p>
      )}

      {shown ? (
        <TransferCard
          transfer={shown}
          decided={shownDecided}
          deciding={deciding}
          actions={
            isRecipient && shown === pending && !shownDecided ? (
              <div className="transfer__actions">
                <button
                  type="button"
                  className="button"
                  aria-disabled={deciding !== undefined}
                  onClick={() => deciding === undefined && decide('accept', pending)}
                >
                  Aceptar custodia
                </button>
                <RejectDialog busy={deciding !== undefined} fallbackFocus={headingRef} onSubmit={(reason) => decide('reject', pending, reason)} />
              </div>
            ) : undefined
          }
        />
      ) : sendingRequest && !checking ? (
        // Ours, not yet the server's: dashed and worded so it never passes for a pending transfer.
        <div className="transfer transfer--sending" aria-hidden="true">
          <PaperPlaneTilt size={20} aria-hidden="true" />
          <div>
            <p className="transfer__status">Enviando…</p>
            <p>
              Solicitud para {nameOf(Number(sendingRequest.get('toCustodianId')))}, aún sin confirmar por el servidor.
            </p>
          </div>
        </div>
      ) : (
        <p className="transfers__none">No hay ninguna transferencia pendiente.</p>
      )}

      {canRequest && (
        <RequestDialog
          custodians={custodians}
          currentCustodianId={detail.currentCustodian.id}
          unresolved={requestIntent ? { toCustodianId: requestIntent.fields.toCustodianId, reason: requestIntent.fields.reason } : undefined}
          busy={request.state !== 'idle'}
          fallbackFocus={headingRef}
          onSubmit={requestTransfer}
        />
      )}
    </section>
  )
}

/** What the page loaded, for checking a write without a known outcome. */
interface Loaded {
  scopes: Record<IntentKind, string>
  chain: ChainEvent[]
  user: SessionUser
  pending: TransferView | null
}

/** A request without a known outcome, checked against the chain the page just read. */
function reconcileRequest({ scopes, chain, user, pending }: Loaded, answer: Unanswered | undefined, focus: boolean): Notice | null {
  const intent = getIntent(scopes.request)
  if (!intent) return focus ? unknownNotice('request', answer, focus, false) : null
  const landed = requestLanded(chain, user, intent)
  if (!landed) return unknownNotice('request', answer, focus, true)
  clearIntent(scopes.request, intent.idempotencyKey)
  if (!focus) return null // on load, the page already shows it
  const text =
    pending?.transferId === landed.transferId
      ? `La solicitud sí llegó: queda pendiente de que ${pending.to.displayName} la acepte.`
      : 'La solicitud sí llegó y ya se resolvió; la cadena de custodia lo muestra.'
  return { kind: 'success', text, focus }
}

/** A decision without a known outcome: the chain shows whether this user decided, and how. */
function reconcileDecision({ scopes, chain, user, pending }: Loaded, answer: Unanswered | undefined, focus: boolean): Notice | null {
  const intent = getIntent(scopes.decision)
  if (!intent) return focus ? unknownNotice('decision', answer, focus, false) : null
  const accepting = intent.fields.decision === 'accept'
  const landed = decisionLanded(chain, user, intent)
  const stillPending = pending?.transferId === Number(intent.fields.transferId)
  if (!landed && stillPending) return unknownNotice('decision', answer, focus, true)
  clearIntent(scopes.decision, intent.idempotencyKey)
  if (!focus) return null
  if (landed === 'same')
    return { kind: 'success', focus, text: accepting ? 'La aceptación sí llegó: ahora la custodia es tuya.' : 'El rechazo sí llegó: la custodia sigue donde estaba.' }
  if (landed === 'other')
    return { kind: 'conflict', focus, text: accepting ? 'No se pudo aceptar: ya la habías rechazado (quizá en otra pestaña).' : 'No se pudo rechazar: ya la habías aceptado (quizá en otra pestaña).' }
  return { kind: 'conflict', focus, text: `No se pudo ${accepting ? 'aceptar' : 'rechazar'}: la transferencia ya no está pendiente.` }
}

/** The chain's request event for this intent: by this user, to that custodian, for that reason, not before it was sent. */
function requestLanded(chain: ChainEvent[], user: SessionUser, intent: PendingIntent): ChainEvent | undefined {
  const since = Date.parse(intent.startedAt) - CLOCK_SKEW_MS
  return chain.find(
    (e) =>
      e.kind === 'TransferRequested' &&
      e.actor.id === user.id &&
      String(e.to?.id) === intent.fields.toCustodianId &&
      e.notes === intent.fields.reason &&
      Date.parse(e.occurredAtUtc) >= since,
  )
}

/** Whether this user decided the intent's transfer: as asked ('same'), the other way, say in another tab ('other'), or not. */
function decisionLanded(chain: ChainEvent[], user: SessionUser, intent: PendingIntent): 'same' | 'other' | undefined {
  const event = chain.find(
    (e) => e.transferId === Number(intent.fields.transferId) && e.actor.id === user.id && (e.kind === 'TransferAccepted' || e.kind === 'TransferRejected'),
  )
  if (!event) return undefined
  return (event.kind === 'TransferAccepted') === (intent.fields.decision === 'accept') ? 'same' : 'other'
}

/** No known outcome, and the chain does not show it: why, and that a retry cannot do it twice. */
function unknownNotice(which: IntentKind, answer: Unanswered | undefined, focus: boolean, canRetry: boolean): Notice {
  const what = which === 'request' ? 'la solicitud' : 'la decisión'
  const why = !answer
    ? `Tu última ${which === 'request' ? 'solicitud' : 'decisión'} quedó sin confirmar y no aparece en la cadena.`
    : answer.problem?.type === PROBLEM.inFlight
      ? `El servidor aún está procesando el primer envío de ${what}.`
      : answer.status === 0
        ? `No hubo respuesta del servidor y ${what} no aparece en la cadena.`
        : `El servidor falló al responder (código ${answer.status}) y ${what} no aparece en la cadena.`
  const next = canRetry ? ' Puedes reintentar: si ya había llegado, no se registrará dos veces.' : ' Vuelve a cargar la página para ver su estado.'
  return { kind: 'unknown', text: why + next, retry: canRetry ? which : undefined, focus }
}

function requestDone(transfer: TransferResource, replayed: boolean): string {
  const arrived = replayed ? 'Esta solicitud ya había llegado' : 'Solicitud enviada'
  const decider = transfer.decidedBy?.displayName ?? transfer.to.displayName
  // The server answers with the transfer as it is now, which a fast recipient may already have decided.
  if (transfer.status === 'Accepted') return `${arrived}, y ${decider} ya la aceptó.`
  if (transfer.status === 'Rejected') return `${arrived}, y ${decider} la rechazó.`
  return `${arrived}: queda pendiente de que ${transfer.to.displayName} la acepte.`
}

/** The pending transfer as the server last said it; after a 409, the decided state that answer carried. */
function TransferCard({
  transfer,
  decided,
  deciding,
  actions,
}: {
  transfer: TransferView
  decided?: Notice['state']
  deciding?: string
  actions?: ReactNode
}) {
  const status = decided?.status ?? 'Pending'
  const by = decided?.actedBy ? ` por ${decided.actedBy.displayName}` : ''
  const Icon = status === 'Accepted' ? CheckCircle : status === 'Rejected' ? XCircle : Hourglass
  return (
    <div className={`transfer transfer--${status.toLowerCase()}`} aria-busy={deciding !== undefined}>
      <Icon size={20} aria-hidden="true" />
      <div>
        <p className="transfer__status">
          {status === 'Pending' && 'Pendiente'}
          {status === 'Accepted' && `Aceptada${by}`}
          {status === 'Rejected' && `Rechazada${by}`}
          {deciding && ` · enviando ${deciding === 'reject' ? 'rechazo' : 'aceptación'}…`}
        </p>
        <p>
          De {transfer.from.displayName} a <strong>{transfer.to.displayName}</strong>, pedida por {transfer.requestedBy.displayName} el{' '}
          {formatUtcDateTime(transfer.requestedAtUtc)}.
        </p>
        <p className="transfer__reason">Motivo: {transfer.reason}</p>
        {actions}
      </div>
    </div>
  )
}

function NoticeIcon({ kind }: { kind: Notice['kind'] }) {
  if (kind === 'success') return <CheckCircle size={18} weight="bold" aria-hidden="true" />
  if (kind === 'unknown') return <Question size={18} weight="bold" aria-hidden="true" />
  return <WarningOctagon size={18} weight="bold" aria-hidden="true" />
}

/** How a failed write's notice starts. */
function failed(write: WriteResult['write']): string {
  return `No se pudo ${write === 'request' ? 'solicitar la transferencia' : write === 'accept' ? 'aceptar' : 'rechazar'}`
}

/** A write turned away before it ran: nothing was saved, and the same key can go again once the wait is over. */
function throttled({ write, retryAfterSeconds }: Extract<WriteResult, { outcome: 'throttled' }>): Notice {
  const text = `${failed(write)}: el servidor recibió demasiadas operaciones seguidas y no guardó nada.`
  return { kind: 'error', focus: true, text, retry: write === 'request' ? 'request' : 'decision', wait: retryAfterSeconds }
}

/** A refused write in the user's terms. A 409 says who did what and when, from the state the server sent back. */
function refusal(
  { write, status, problem }: Extract<WriteResult, { outcome: 'refused' }>,
  user: SessionUser,
  nameOf: (id: number) => string,
  decidedOn: TransferView | null,
): Notice {
  const fail = failed(write)
  const current = problem?.currentState as { transferId: number; status: TransferStatus; toCustodianId: number } | undefined
  if (status === 409 && current) {
    const actedBy = problem?.actedBy as PersonRef | undefined
    const actedAtUtc = problem?.actedAtUtc as string | undefined
    const self = actedBy?.id === user.id
    const who = actedBy?.displayName ?? 'otra persona'
    const when = actedAtUtc ? ` el ${formatUtcDateTime(actedAtUtc)}` : ''
    const transfer = decidedOn?.transferId === current.transferId ? decidedOn : undefined
    const state = transfer && current.status !== 'Pending' ? { transfer, status: current.status, actedBy } : undefined
    const conflict = (text: string): Notice => ({ kind: 'conflict', focus: true, state, text: `${fail}: ${text}` })
    if (problem?.type === PROBLEM.staleVersion) return conflict('la transferencia cambió desde que cargaste la página. Abajo está su estado actual.')
    if (current.status === 'Accepted') return conflict(self ? `ya la aceptaste${when} (quizá en otra pestaña).` : `${who} ya la aceptó${when}.`)
    if (current.status === 'Rejected') return conflict(self ? `ya la rechazaste${when} (quizá en otra pestaña).` : `${who} ya la rechazó${when}.`)
    return conflict(`ya hay una transferencia pendiente para ${nameOf(current.toCustodianId)}, pedida por ${self ? 'ti' : who}${when}.`)
  }
  const error = (text: string, retry?: IntentKind): Notice => ({ kind: 'error', focus: true, text: `${fail}${text}`, retry })
  if (problem?.type === PROBLEM.concurrentWrite)
    return error(': otra operación sobre esta evidencia coincidió con la tuya y no se guardó nada. Puedes reintentar.', write === 'request' ? 'request' : 'decision')
  if (status === 409) return { kind: 'conflict', focus: true, text: `${fail}: el estado cambió mientras tanto. La página ya muestra el actual.` }
  if (status === 403) return error(write === 'request' ? ': solo un investigador puede pedirla.' : ': solo el custodio que la recibe puede decidirla.')
  if (status === 422) return error(': esa petición ya se había usado con otros datos. Vuelve a intentarlo.')
  // The API's own wording is English and technical; the reloaded page shows what changed.
  if (status === 400) return error(': el servidor no aceptó los datos. La página ya muestra el estado actual; revísalo y vuelve a intentarlo.')
  return error(` (código ${status}).`)
}

/** Calls `handle` once per answer, when the fetcher is idle again, so the page's re-read has already landed. */
function useSettled(fetcher: { state: string; data?: WriteResult }, handle: (result: WriteResult) => void) {
  const handled = useRef<WriteResult | undefined>(undefined)
  const onSettled = useEffectEvent(handle) // the latest props, without re-running the effect for them
  useEffect(() => {
    if (fetcher.state === 'idle' && fetcher.data && fetcher.data !== handled.current) {
      handled.current = fetcher.data
      onSettled(fetcher.data)
    }
  }, [fetcher.state, fetcher.data])
}
