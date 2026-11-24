import { useMemo } from 'react';
import type { Relationship, SchemaNode } from '../../api/types';
import { EChart } from '../../components/EChart';
import { EmptyState } from '../../components/EmptyState';

/** Force-directed graph of tables (nodes) and foreign keys (edges). */
export function RelationshipGraph({ schemas, relationships, highlight }: { schemas: SchemaNode[]; relationships: Relationship[]; highlight?: string | null }) {
  const option = useMemo(() => {
    const related = new Set(relationships.flatMap((r) => [r.fromTable, r.toTable]));
    const nodes = schemas.flatMap((s) => s.tables)
      .filter((t) => related.has(`${t.schemaName}.${t.tableName}`) || relationships.length === 0)
      .map((t) => {
        const id = `${t.schemaName}.${t.tableName}`;
        return {
          id,
          name: id,
          symbolSize: 18 + Math.min(t.columnCount, 30),
          value: t.columnCount,
          itemStyle: { opacity: t.isAllowed ? 1 : 0.35, borderWidth: id === highlight ? 3 : 0, borderColor: '#f2b84b' },
          label: { show: true },
        };
      });
    return {
      tooltip: { formatter: '{b}' },
      series: [{
        type: 'graph',
        layout: 'force',
        roam: true,
        draggable: true,
        force: { repulsion: 260, edgeLength: [80, 160] },
        edgeSymbol: ['none', 'arrow'],
        edgeSymbolSize: 8,
        label: { position: 'right', fontSize: 11 },
        lineStyle: { curveness: 0.15, opacity: 0.7 },
        data: nodes,
        links: relationships.map((r) => ({ source: r.fromTable, target: r.toTable, name: `${r.fromColumn} → ${r.toColumn}` })),
        emphasis: { focus: 'adjacency' },
      }],
    };
  }, [schemas, relationships, highlight]);

  if (relationships.length === 0) return <EmptyState title="No relationships" description="No foreign keys were discovered between permitted tables." />;
  return <EChart option={option} height={520} ariaLabel="Table relationship diagram" />;
}
