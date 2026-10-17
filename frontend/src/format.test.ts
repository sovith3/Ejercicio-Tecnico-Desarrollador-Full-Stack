import { describe, expect, it } from 'vitest'
import { formatDate, formatMoney, formatNumber, statusLabel, statusTone } from './format'

describe('format', () => {
  it('muestra los montos en pesos enteros con punto de miles', () => {
    expect(formatMoney(421254)).toMatch(/421[.\s]?254/)
    expect(formatMoney(1234567)).toMatch(/1[.\s]?234[.\s]?567/)
    expect(formatMoney(undefined)).toContain('0')
  })

  it('redondea a la unidad usando el primer decimal', () => {
    expect(formatMoney(1234.4)).toMatch(/1[.\s]?234(?!,)/)
    expect(formatMoney(1234.5)).toMatch(/1[.\s]?235(?!,)/)
    expect(formatMoney(1234567.891)).toMatch(/1[.\s]?234[.\s]?568$/)
    expect(formatMoney(265018.99999)).toMatch(/265[.\s]?019$/)
    expect(formatMoney(0.00001)).toMatch(/0$/)
  })

  it('no expone decimales aunque la API los entregue', () => {
    expect(formatMoney(1234.56)).not.toContain(',')
    expect(formatMoney(265018.99999)).not.toContain(',')
  })

  it('formatea numeros y fechas ISO', () => {
    expect(formatNumber(1234)).toMatch(/1[.,\s]?234/)
    expect(formatDate('2025-02-18')).toBe('18-02-2025')
    expect(formatDate(null)).toBe('—')
  })

  it('traduce los estados del dominio y tolera valores vacios', () => {
    expect(statusLabel('Overdue')).toBe('Vencida')
    expect(statusLabel('Paid')).toBe('Pagada')
    expect(statusLabel(undefined)).toBe('—')
  })

  it('asigna un tono visual a cada estado', () => {
    expect(statusTone('Paid')).toBe('good')
    expect(statusTone('Overdue')).toBe('bad')
    expect(statusTone('Partial')).toBe('warn')
    expect(statusTone('desconocido')).toBe('neutral')
  })
})
