import { buildChartOption, buildSeries, initialChartConfig, validateChartConfig } from './chart';
import type { ResultColumn } from '../api/types';

const columns: ResultColumn[] = [
  { name: 'Region', dataType: 'nvarchar', clrType: 'String' },
  { name: 'OrderMonth', dataType: 'date', clrType: 'DateTime' },
  { name: 'Revenue', dataType: 'decimal', clrType: 'Decimal' },
];

describe('chart configuration', () => {
  it('accepts a valid AI suggestion', () => {
    const config = initialChartConfig(columns, { recommended: true, chartType: 'line', xAxis: 'ordermonth', yAxis: 'revenue', aggregation: 'none', source: 'ai' });
    expect(config).toEqual({ chartType: 'line', xAxis: 'OrderMonth', yAxis: 'Revenue', aggregation: 'none' });
  });

  it('rejects suggestions referencing unknown columns and falls back to column types', () => {
    const config = initialChartConfig(columns, { recommended: true, chartType: 'bar', xAxis: 'Hacked', yAxis: 'Revenue', aggregation: 'none', source: 'ai' });
    expect(config).toEqual({ chartType: 'line', xAxis: 'OrderMonth', yAxis: 'Revenue', aggregation: 'none' });
  });

  it('validates types, axes and chart kinds', () => {
    expect(validateChartConfig({ chartType: 'bar', xAxis: 'Region', yAxis: 'Region', aggregation: 'sum' }, columns).valid).toBe(false);
    expect(validateChartConfig({ chartType: 'bar', xAxis: 'Region', yAxis: 'Region', aggregation: 'count' }, columns).valid).toBe(true);
    expect(validateChartConfig({ chartType: 'evil' as never, xAxis: 'Region', yAxis: 'Revenue', aggregation: 'none' }, columns).valid).toBe(false);
    expect(validateChartConfig({ chartType: 'scatter', xAxis: 'Region', yAxis: 'Revenue', aggregation: 'none' }, columns).valid).toBe(false);
  });

  it('aggregates rows by the x-axis', () => {
    const rows = [['East', null, 10], ['West', null, '5.5'], ['East', null, 20], ['West', null, null]];
    expect(buildSeries(rows, columns, { chartType: 'bar', xAxis: 'Region', yAxis: 'Revenue', aggregation: 'sum' })).toMatchObject({ categories: ['East', 'West'], values: [30, 5.5] });
    expect(buildSeries(rows, columns, { chartType: 'bar', xAxis: 'Region', yAxis: 'Revenue', aggregation: 'count' }).values).toEqual([2, 2]);
    expect(buildSeries(rows, columns, { chartType: 'bar', xAxis: 'Region', yAxis: 'Revenue', aggregation: 'avg' }).values).toEqual([15, 5.5]);
  });

  it('produces a data-only option object', () => {
    const option = buildChartOption({ chartType: 'pie', xAxis: 'Region', yAxis: 'Revenue', aggregation: 'none' }, { categories: ['A'], values: [1], points: [] });
    expect(JSON.stringify(option)).toContain('"type":"pie"');
    expect(Object.values(option).some((v) => typeof v === 'function')).toBe(false);
  });
});
