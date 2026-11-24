import type { CellValue, ResultColumn } from '../api/types';

/**
 * Neutralises spreadsheet formula injection: values beginning with = + - @ (or tab/CR) are prefixed with a quote
 * so that opening an export in Excel never evaluates query data as a formula.
 */
export function sanitizeCell(value: CellValue): string {
  if (value === null || value === undefined) return '';
  const s = String(value);
  return /^[=+\-@\t\r]/.test(s) && typeof value === 'string' ? `'${s}` : s;
}

export function toCsv(columns: ResultColumn[], rows: CellValue[][]): string {
  const escape = (v: string) => (/[",\r\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
  const lines = [columns.map((c) => escape(c.name)).join(',')];
  for (const row of rows) lines.push(row.map((v) => escape(sanitizeCell(v))).join(','));
  return '﻿' + lines.join('\r\n');
}

export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function safeFileName(title: string, extension: string) {
  const base = title.replace(/[^\w\- ]+/g, '').trim().replace(/\s+/g, '_').slice(0, 60) || 'query_results';
  return `${base}_${new Date().toISOString().slice(0, 10)}.${extension}`;
}

export function exportCsv(title: string, columns: ResultColumn[], rows: CellValue[][]) {
  downloadBlob(new Blob([toCsv(columns, rows)], { type: 'text/csv;charset=utf-8' }), safeFileName(title, 'csv'));
}

/** Excel export (exceljs is loaded on demand to keep the main bundle small). */
export async function exportExcel(title: string, columns: ResultColumn[], rows: CellValue[][]) {
  const { default: ExcelJS } = await import('exceljs');
  const workbook = new ExcelJS.Workbook();
  workbook.creator = 'AI SQL Query Generator';
  const sheet = workbook.addWorksheet('Results');
  sheet.columns = columns.map((c) => ({ header: c.name, key: c.name, width: Math.min(Math.max(c.name.length + 4, 12), 50) }));
  sheet.getRow(1).font = { bold: true };
  for (const row of rows) {
    sheet.addRow(row.map((v, i) => {
      if (v === null) return null;
      const col = columns[i];
      if (typeof v === 'number') return v;
      if (typeof v === 'boolean') return v;
      if (/^(decimal|numeric|money|bigint)$/i.test(col.dataType) && /^-?\d+(\.\d+)?$/.test(v)) return Number(v);
      return sanitizeCell(v);
    }));
  }
  sheet.views = [{ state: 'frozen', ySplit: 1 }];
  const buffer = await workbook.xlsx.writeBuffer();
  downloadBlob(new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), safeFileName(title, 'xlsx'));
}
