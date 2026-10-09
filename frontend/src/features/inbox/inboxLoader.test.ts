import { describe, expect, it } from 'vitest'
import { describeEmpty, describeResults } from '../../lib/format'
import { readFilter } from './inboxLoader'

describe('readFilter', () => {
  it('trims q and keeps known types', () => {
    expect(readFilter(new URLSearchParams('q=%20mail%20&type=EML'))).toEqual({ q: 'mail', type: 'EML' })
  })

  it('reads the custodian, status, sort and cursor, ignoring anything it does not know', () => {
    expect(readFilter(new URLSearchParams('custodianId=5&status=Invalid&sort=lastEventAt:asc&cursor=c1'))).toEqual({ custodianId: 5, status: 'Invalid', sort: 'lastEventAt:asc', cursor: 'c1' })
    expect(readFilter(new URLSearchParams('custodianId=-1&status=valid&sort=code'))).toEqual({})
  })

  it('ignores blank q and unknown or lowercase types', () => {
    expect(readFilter(new URLSearchParams('q=%20&type=PDF'))).toEqual({ q: undefined, type: undefined })
    expect(readFilter(new URLSearchParams('type=log'))).toEqual({ q: undefined, type: undefined })
  })
})

describe('result wording', () => {
  it('describes counts with their filters', () => {
    expect(describeResults(1, {})).toBe('1 evidencia')
    expect(describeResults(3, { q: 'vpn', type: 'LOG' })).toBe('3 evidencias de tipo LOG para «vpn»')
  })

  it('names the custodian, the status and whether more pages follow', () => {
    expect(describeResults(25, { status: 'Valid' }, { custodian: 'Diego Salas', more: true })).toBe(
      '25 evidencias en custodia de Diego Salas con integridad «Íntegra»; hay más en la página siguiente',
    )
  })

  it('explains empty results by filter', () => {
    expect(describeEmpty({ custodianId: 4 })).toBe('Ninguna evidencia coincide con los filtros.')
    expect(describeEmpty({})).toBe('Aún no hay evidencias registradas.')
    expect(describeEmpty({ type: 'CSV' })).toBe('No hay evidencias de tipo CSV.')
    expect(describeEmpty({ q: 'zzz', type: 'EML' })).toBe('Sin resultados para «zzz» en EML.')
  })
})
