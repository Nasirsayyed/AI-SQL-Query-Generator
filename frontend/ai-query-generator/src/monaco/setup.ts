// Self-hosted Monaco: bundled by Vite (no CDN), with only the editor core and the SQL language.
import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/sql/register';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import { loader } from '@monaco-editor/react';

self.MonacoEnvironment = { getWorker: () => new EditorWorker() };
loader.config({ monaco });

export { monaco };
