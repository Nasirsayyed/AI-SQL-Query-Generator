import { useEffect, useRef } from 'react';
import Editor, { type OnMount } from '@monaco-editor/react';
import { Box, useTheme } from '@mui/material';
import { monaco } from '../monaco/setup';
import type { CompletionTable } from '../api/types';

const KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'GROUP BY', 'ORDER BY', 'HAVING', 'JOIN', 'INNER JOIN', 'LEFT JOIN', 'RIGHT JOIN', 'FULL OUTER JOIN',
  'CROSS APPLY', 'OUTER APPLY', 'ON', 'AND', 'OR', 'NOT', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'IS NULL', 'IS NOT NULL', 'AS', 'DISTINCT',
  'TOP', 'OFFSET', 'FETCH NEXT', 'ROWS ONLY', 'UNION ALL', 'WITH', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'OVER', 'PARTITION BY',
];
const FUNCTIONS = [
  'COUNT', 'SUM', 'AVG', 'MIN', 'MAX', 'STRING_AGG', 'ROW_NUMBER', 'RANK', 'DENSE_RANK', 'LAG', 'LEAD', 'GETDATE', 'SYSDATETIME',
  'DATEADD', 'DATEDIFF', 'DATEPART', 'DATENAME', 'DATEFROMPARTS', 'EOMONTH', 'YEAR', 'MONTH', 'DAY', 'FORMAT', 'CAST', 'CONVERT',
  'ISNULL', 'COALESCE', 'NULLIF', 'IIF', 'ROUND', 'ABS', 'LEN', 'UPPER', 'LOWER', 'TRIM', 'SUBSTRING', 'REPLACE', 'CONCAT',
];

export interface SqlEditorProps {
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
  height?: number | string;
  completions?: CompletionTable[];
  onExecuteShortcut?: () => void;
}

/** Monaco SQL editor with schema-aware completion of permitted tables and columns. */
export default function SqlEditor({ value, onChange, readOnly, height = 280, completions = [], onExecuteShortcut }: SqlEditorProps) {
  const theme = useTheme();
  const completionsRef = useRef(completions);
  const executeRef = useRef(onExecuteShortcut);
  completionsRef.current = completions;
  executeRef.current = onExecuteShortcut;

  useEffect(() => {
    const disposable = monaco.languages.registerCompletionItemProvider('sql', {
      triggerCharacters: ['.', ' '],
      provideCompletionItems(model, position) {
        const word = model.getWordUntilPosition(position);
        const range = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);
        const before = model.getLineContent(position.lineNumber).slice(0, word.startColumn - 1);
        const Kind = monaco.languages.CompletionItemKind;
        const tables = completionsRef.current;

        // After "alias." or "Table." suggest that table's columns.
        const qualifier = /([\w\]]+)\.$/.exec(before)?.[1]?.replace(/[[\]]/g, '');
        if (qualifier) {
          const text = model.getValue();
          const aliasMatch = new RegExp(`(?:\\[?(\\w+)\\]?\\.)?\\[?(\\w+)\\]?\\s+(?:AS\\s+)?${qualifier}\\b`, 'i').exec(text);
          const tableName = aliasMatch?.[2] ?? qualifier;
          const table = tables.find((t) => t.tableName.toLowerCase() === tableName.toLowerCase());
          if (table)
            return {
              suggestions: table.columns.map((c) => ({ label: c.name, kind: Kind.Field, detail: c.dataType, insertText: c.name, range })),
            };
          const schemaTables = tables.filter((t) => t.schemaName.toLowerCase() === qualifier.toLowerCase());
          if (schemaTables.length)
            return { suggestions: schemaTables.map((t) => ({ label: t.tableName, kind: Kind.Struct, detail: t.objectType, insertText: t.tableName, range })) };
        }

        return {
          suggestions: [
            ...tables.map((t) => ({ label: `${t.schemaName}.${t.tableName}`, kind: Kind.Struct, detail: `${t.objectType} · ${t.columns.length} columns`, insertText: `[${t.schemaName}].[${t.tableName}]`, range })),
            ...tables.flatMap((t) => t.columns.map((c) => ({ label: c.name, kind: Kind.Field, detail: `${t.tableName} · ${c.dataType}`, insertText: c.name, range, sortText: `z${c.name}` }))),
            ...KEYWORDS.map((k) => ({ label: k, kind: Kind.Keyword, insertText: k, range })),
            ...FUNCTIONS.map((f) => ({ label: f, kind: Kind.Function, insertText: `${f}($0)`, insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, range })),
          ],
        };
      },
    });
    return () => disposable.dispose();
  }, []);

  const handleMount: OnMount = (editor) => {
    editor.addAction({
      id: 'aiqg.execute',
      label: 'Execute query',
      keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter],
      run: () => executeRef.current?.(),
    });
  };

  return (
    <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
      <Editor
        height={height}
        language="sql"
        value={value}
        onChange={(v) => onChange(v ?? '')}
        onMount={handleMount}
        theme={theme.palette.mode === 'dark' ? 'vs-dark' : 'vs'}
        options={{
          readOnly,
          minimap: { enabled: false },
          fontSize: 13,
          lineNumbers: 'on',
          automaticLayout: true,
          scrollBeyondLastLine: false,
          wordWrap: 'on',
          tabSize: 4,
          autoIndent: 'full',
          formatOnPaste: false,
          renderLineHighlight: 'line',
          quickSuggestions: { other: true, strings: false, comments: false },
          ariaLabel: 'SQL editor',
        }}
      />
    </Box>
  );
}
