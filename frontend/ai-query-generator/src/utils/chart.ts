import type { Aggregation, CellValue, ChartSuggestion, ChartType, ResultColumn } from '../api/types';

export const chartTypes: { value: ChartType; label: string }[] = [
  { value: 'bar', label: 'Bar' },
  { value: 'horizontalBar', label: 'Horizontal bar' },
  { value: 'line', label: 'Line' },
  { value: 'area', label: 'Area' },
  { value: 'pie', label: 'Pie' },
  { value: 'doughnut', label: 'Doughnut' },
  { value: 'scatter', label: 'Scatter' },
];

export const aggregations: Aggregation[] = ['none', 'sum', 'avg', 'count', 'min', 'max'];

export interface ChartConfig {
  chartType: ChartType;
  xAxis: string;
  yAxis: string;
  aggregation: Aggregation;
}

const NUMERIC = new Set(['int', 'bigint', 'smallint', 'tinyint', 'decimal', 'numeric', 'money', 'smallmoney', 'float', 'real', 'Int32', 'Int64', 'Int16', 'Byte', 'Decimal', 'Double', 'Single']);
const DATES = new Set(['date', 'datetime', 'datetime2', 'smalldatetime', 'datetimeoffset', 'DateTime', 'DateTimeOffset']);

export const isNumericColumn = (c: ResultColumn) => NUMERIC.has(c.dataType) || NUMERIC.has(c.clrType);
export const isDateColumn = (c: ResultColumn) => DATES.has(c.dataType) || DATES.has(c.clrType);

/**
 * Validates a chart configuration (from the AI or the user) against the actual result columns.
 * Anything that does not reference real columns with suitable types is rejected.
 */
export function validateChartConfig(config: Partial<ChartConfig> | null | undefined, columns: ResultColumn[]): { valid: boolean; errors: string[] } {
  const errors: string[] = [];
  if (!config) return { valid: false, errors: ['No chart configuration.'] };
  if (!chartTypes.some((t) => t.value === config.chartType)) errors.push('Unsupported chart type.');
  if (!config.aggregation || !aggregations.includes(config.aggregation)) errors.push('Unsupported aggregation.');
  const x = columns.find((c) => c.name === config.xAxis);
  const y = columns.find((c) => c.name === config.yAxis);
  if (!x) errors.push('X-axis must be a result column.');
  if (!y) errors.push('Y-axis must be a result column.');
  if (y && !isNumericColumn(y) && config.aggregation !== 'count') errors.push('Y-axis must be numeric (or use the count aggregation).');
  if (config.chartType === 'scatter' && x && !isNumericColumn(x)) errors.push('Scatter charts need a numeric X-axis.');
  return { valid: errors.length === 0, errors };
}

/** Builds a safe default configuration from an AI suggestion or the column types. */
export function initialChartConfig(columns: ResultColumn[], suggestion?: ChartSuggestion | null): ChartConfig | null {
  if (suggestion?.recommended && suggestion.chartType && suggestion.xAxis && suggestion.yAxis) {
    const candidate: ChartConfig = {
      chartType: suggestion.chartType,
      xAxis: columns.find((c) => c.name.toLowerCase() === suggestion.xAxis!.toLowerCase())?.name ?? suggestion.xAxis,
      yAxis: columns.find((c) => c.name.toLowerCase() === suggestion.yAxis!.toLowerCase())?.name ?? suggestion.yAxis,
      aggregation: suggestion.aggregation ?? 'none',
    };
    if (validateChartConfig(candidate, columns).valid) return candidate;
  }
  const y = columns.find(isNumericColumn);
  const x = columns.find((c) => isDateColumn(c)) ?? columns.find((c) => !isNumericColumn(c)) ?? columns.find((c) => c !== y);
  if (!x || !y || x === y) return null;
  return { chartType: isDateColumn(x) ? 'line' : 'bar', xAxis: x.name, yAxis: y.name, aggregation: 'none' };
}

const toNumber = (v: CellValue): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

const label = (v: CellValue): string => (v === null ? '(null)' : typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T00:00:00/.test(v) ? v.slice(0, 10) : String(v));

/** Aggregates result rows into category/value series using only the returned query data. */
export function buildSeries(rows: CellValue[][], columns: ResultColumn[], config: ChartConfig, maxPoints = 500) {
  const xi = columns.findIndex((c) => c.name === config.xAxis);
  const yi = columns.findIndex((c) => c.name === config.yAxis);
  if (xi < 0 || yi < 0) return { categories: [] as string[], values: [] as number[], points: [] as [number, number][] };

  if (config.chartType === 'scatter') {
    const points = rows.map((r) => [toNumber(r[xi]), toNumber(r[yi])] as const).filter((p): p is readonly [number, number] => p[0] !== null && p[1] !== null).slice(0, 2000);
    return { categories: [], values: [], points: points.map((p) => [p[0], p[1]] as [number, number]) };
  }

  if (config.aggregation === 'none') {
    const slice = rows.slice(0, maxPoints);
    return { categories: slice.map((r) => label(r[xi])), values: slice.map((r) => toNumber(r[yi]) ?? 0), points: [] };
  }

  const groups = new Map<string, number[]>();
  for (const r of rows) {
    const key = label(r[xi]);
    const n = toNumber(r[yi]);
    if (!groups.has(key)) groups.set(key, []);
    if (config.aggregation === 'count' || n !== null) groups.get(key)!.push(n ?? 0);
  }
  const reduce = (values: number[]) => {
    switch (config.aggregation) {
      case 'count': return values.length;
      case 'avg': return values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0;
      case 'min': return values.length ? Math.min(...values) : 0;
      case 'max': return values.length ? Math.max(...values) : 0;
      default: return values.reduce((a, b) => a + b, 0);
    }
  };
  const entries = [...groups.entries()].slice(0, maxPoints);
  return { categories: entries.map(([k]) => k), values: entries.map(([, v]) => Math.round(reduce(v) * 100) / 100), points: [] };
}

/** Produces a declarative ECharts option object (data only — no functions or formatter code). */
export function buildChartOption(config: ChartConfig, series: ReturnType<typeof buildSeries>, title?: string | null) {
  const yName = config.aggregation === 'none' ? config.yAxis : `${config.aggregation.toUpperCase()}(${config.yAxis})`;
  const base = {
    title: title ? { text: title, left: 'center', textStyle: { fontSize: 14 } } : undefined,
    tooltip: { trigger: config.chartType === 'pie' || config.chartType === 'doughnut' || config.chartType === 'scatter' ? 'item' : 'axis' },
    grid: { left: 16, right: 24, top: title ? 48 : 24, bottom: 16, containLabel: true },
  };

  switch (config.chartType) {
    case 'pie':
    case 'doughnut':
      return {
        ...base,
        legend: { type: 'scroll', bottom: 0 },
        series: [{
          type: 'pie', name: yName, radius: config.chartType === 'doughnut' ? ['45%', '72%'] : '72%',
          data: series.categories.map((name, i) => ({ name, value: series.values[i] })),
        }],
      };
    case 'scatter':
      return {
        ...base,
        xAxis: { type: 'value', name: config.xAxis, scale: true },
        yAxis: { type: 'value', name: config.yAxis, scale: true },
        series: [{ type: 'scatter', name: yName, data: series.points, symbolSize: 8 }],
      };
    case 'horizontalBar':
      return {
        ...base,
        xAxis: { type: 'value', name: yName },
        yAxis: { type: 'category', data: series.categories, inverse: true, axisLabel: { width: 160, overflow: 'truncate' } },
        series: [{ type: 'bar', name: yName, data: series.values }],
        dataZoom: series.categories.length > 25 ? [{ type: 'slider', yAxisIndex: 0, right: 0 }] : undefined,
      };
    default:
      return {
        ...base,
        xAxis: {
          type: 'category',
          data: series.categories,
          axisLabel: { interval: series.categories.length <= 40 ? 0 : 'auto', rotate: series.categories.length > 6 ? 35 : 0, width: 120, overflow: 'truncate' },
        },
        yAxis: { type: 'value', name: yName },
        series: [{
          type: config.chartType === 'bar' ? 'bar' : 'line',
          name: yName,
          data: series.values,
          smooth: config.chartType !== 'bar',
          areaStyle: config.chartType === 'area' ? {} : undefined,
        }],
        dataZoom: series.categories.length > 40 ? [{ type: 'slider', bottom: 0 }] : undefined,
      };
  }
}
