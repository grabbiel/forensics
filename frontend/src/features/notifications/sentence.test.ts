import { describe, expect, it } from 'vitest'
import type { NotificationItem } from '../../api/notifications'
import { DEMO } from '../../test/session'
import { diego, lucia, nuria, person } from '../../test/fixtures'
import { sentenceFor, sentenceText, stanceOf } from './sentence'

const request: NotificationItem = {
  notificationId: 9, kind: 'TransferRequested', createdAtUtc: '2026-10-08T10:00:00Z', readAtUtc: null, evidenceCode: 'LOG202609110007',
  transferId: 7, requestedBy: lucia, from: nuria, to: diego, reason: 'Peritaje externo', decisionNotes: null,
}
const accepted: NotificationItem = { ...request, kind: 'TransferAccepted', decisionNotes: 'Recibida' }
const rejected: NotificationItem = { ...request, kind: 'TransferRejected', decisionNotes: 'Falta el acta' }

const lucíaReads = DEMO.Investigador // id 1: the requester
const diegoReads = DEMO.Custodio // id 4: the recipient
const nuriaReads = { id: nuria.id, userName: 'nuria', displayName: nuria.displayName, role: 'Custodio' as const }
const elenaReads = DEMO.Supervisor // id 10: no party

const text = (item: NotificationItem, viewer = elenaReads) => sentenceText(sentenceFor(item, viewer).parts)

describe('a notification read by', () => {
  it('the recipient asks them to take the evidence and says who holds it', () => {
    expect(stanceOf(request, diegoReads)).toBe('recipient')
    expect(text(request, diegoReads)).toBe('Lucía Ferrer solicitó transferirte LOG202609110007, ahora en custodia de Nuria Paredes.')
  })

  it('the holder asks them to hand it over', () => {
    expect(stanceOf(request, nuriaReads)).toBe('holder')
    expect(text(request, nuriaReads)).toBe('Lucía Ferrer solicitó que entregues LOG202609110007 a Diego Salas.')
  })

  it('the requester tells them the answer, with a rejection’s reason', () => {
    expect(stanceOf(accepted, lucíaReads)).toBe('requester')
    expect(text(accepted, lucíaReads)).toBe('Diego Salas aceptó tu solicitud: LOG202609110007 pasa a su custodia.')
    expect(text(rejected, lucíaReads)).toBe('Diego Salas rechazó tu solicitud de transferir LOG202609110007. Motivo: Falta el acta')
  })

  it('a Supervisor names every party in the third person', () => {
    expect(stanceOf(request, elenaReads)).toBe('supervisor')
    expect(text(request)).toBe('Lucía Ferrer solicitó transferir LOG202609110007 de Nuria Paredes a Diego Salas.')
    expect(text(accepted)).toBe('Diego Salas aceptó la transferencia de LOG202609110007 solicitada por Lucía Ferrer.')
    expect(text(rejected)).toBe('Diego Salas rechazó la transferencia de LOG202609110007 solicitada por Lucía Ferrer. Motivo: Falta el acta')
  })

  it('a party the table has no row for reads as a Supervisor', () => {
    // A Supervisor who was the holder is told of the decision as a Supervisor.
    const holdingSupervisor = { ...elenaReads, id: nuria.id }
    expect(text(accepted, holdingSupervisor)).toBe(text(accepted))
  })
})

describe('the sentence', () => {
  it('shows the request’s reason under a request, and nothing under a decision', () => {
    expect(sentenceFor(request, diegoReads).detail).toBe('Peritaje externo')
    expect(sentenceFor(accepted, lucíaReads).detail).toBeUndefined()
    expect(sentenceFor(rejected, lucíaReads).detail).toBeUndefined()
  })

  it('puts names and the code in bold', () => {
    expect(sentenceFor(request, nuriaReads).parts).toEqual([{ strong: 'Lucía Ferrer' }, ' solicitó que entregues ', { strong: 'LOG202609110007' }, ' a ', { strong: 'Diego Salas' }, '.'])
  })

  it('says no reason rather than "null" when a rejection has none', () => {
    expect(text({ ...rejected, decisionNotes: null, to: person(4, 'Diego Salas') })).toBe('Diego Salas rechazó la transferencia de LOG202609110007 solicitada por Lucía Ferrer.')
  })
})
