import { describe, expect, it } from 'vitest'
import { describeEmpty, describeResults } from '../../lib/format'
import { readFilter } from './inboxLoader'

describe('readFilter', () => {
  it('trims q and keeps known types', () => {
    expect(readFilter(new URLSearchParams('q=%20mail%20&type=EML'))).toEqual({ q: 'mail', type: 'EML' })
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

  it('explains empty results by filter', () => {
    expect(describeEmpty({})).toBe('Aún no hay evidencias registradas.')
    expect(describeEmpty({ type: 'CSV' })).toBe('No hay evidencias de tipo CSV.')
    expect(describeEmpty({ q: 'zzz', type: 'EML' })).toBe('Sin resultados para «zzz» en EML.')
  })
})
