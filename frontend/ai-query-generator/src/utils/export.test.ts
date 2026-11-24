import { sanitizeCell, toCsv } from './export';

describe('CSV export', () => {
  it('escapes quotes, commas and newlines', () => {
    const csv = toCsv([{ name: 'Name', dataType: 'nvarchar', clrType: 'String' }, { name: 'Note', dataType: 'nvarchar', clrType: 'String' }], [['A, B', 'say "hi"\nok'], [null, 'x']]);
    expect(csv).toBe('﻿Name,Note\r\n"A, B","say ""hi""\nok"\r\n,x');
  });

  it('neutralises spreadsheet formula injection', () => {
    expect(sanitizeCell('=HYPERLINK("http://evil")')).toBe('\'=HYPERLINK("http://evil")');
    expect(sanitizeCell('+1')).toBe("'+1");
    expect(sanitizeCell('@SUM(A1)')).toBe("'@SUM(A1)");
    expect(sanitizeCell(-5)).toBe('-5');
    expect(sanitizeCell('normal')).toBe('normal');
  });
});
