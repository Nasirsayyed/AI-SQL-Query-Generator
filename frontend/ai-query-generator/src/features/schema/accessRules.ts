import type { AllowedObjectRule } from '../../api/types';

export type TableAccess = 'inherit' | 'allow' | 'deny';

const lc = (s?: string | null) => (s ?? '').toLowerCase();

/** Client-side mirror of the server's AccessRuleEvaluator, used to preview edits before saving. */
export function evaluate(rules: AllowedObjectRule[]) {
  const schemaAllowed = (s: string) => rules.some((r) => r.objectType === 'Schema' && r.accessLevel === 'Allow' && lc(r.schemaName) === lc(s));
  const schemaDenied = (s: string) => rules.some((r) => r.objectType === 'Schema' && r.accessLevel === 'Deny' && lc(r.schemaName) === lc(s));
  const tableRule = (s: string, t: string): TableAccess => {
    const r = rules.find((x) => (x.objectType === 'Table' || x.objectType === 'View') && lc(x.schemaName) === lc(s) && lc(x.objectName) === lc(t));
    return r ? (r.accessLevel === 'Allow' ? 'allow' : 'deny') : 'inherit';
  };
  const tableAllowed = (s: string, t: string) => {
    const rule = tableRule(s, t);
    if (rule === 'deny' || schemaDenied(s)) return false;
    return rule === 'allow' || schemaAllowed(s);
  };
  const columnDenied = (s: string, t: string, c: string) =>
    rules.some((r) => r.objectType === 'Column' && r.accessLevel === 'Deny' && lc(r.schemaName) === lc(s) && lc(r.objectName) === lc(t) && lc(r.columnName) === lc(c));
  return { schemaAllowed, tableRule, tableAllowed, columnDenied };
}

export function setSchemaAllowed(rules: AllowedObjectRule[], schema: string, allowed: boolean): AllowedObjectRule[] {
  const rest = rules.filter((r) => !(r.objectType === 'Schema' && lc(r.schemaName) === lc(schema)));
  return allowed ? [...rest, { objectType: 'Schema', schemaName: schema, accessLevel: 'Allow' }] : rest;
}

export function setTableAccess(rules: AllowedObjectRule[], schema: string, table: string, objectType: 'Table' | 'View', access: TableAccess): AllowedObjectRule[] {
  const rest = rules.filter((r) => !((r.objectType === 'Table' || r.objectType === 'View') && lc(r.schemaName) === lc(schema) && lc(r.objectName) === lc(table)));
  return access === 'inherit' ? rest : [...rest, { objectType, schemaName: schema, objectName: table, accessLevel: access === 'allow' ? 'Allow' : 'Deny' }];
}

export function setColumnDenied(rules: AllowedObjectRule[], schema: string, table: string, column: string, denied: boolean): AllowedObjectRule[] {
  const rest = rules.filter((r) => !(r.objectType === 'Column' && lc(r.schemaName) === lc(schema) && lc(r.objectName) === lc(table) && lc(r.columnName) === lc(column)));
  return denied ? [...rest, { objectType: 'Column', schemaName: schema, objectName: table, columnName: column, accessLevel: 'Deny' }] : rest;
}
