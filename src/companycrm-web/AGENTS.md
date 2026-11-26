# companycrm-web — Agent Guide

React 19 + TypeScript + Vite 6 + Material UI (MUI v6) SPA.

## Commands
- `npm run dev` — Start Vite dev server (`http://localhost:5173`).
- `npm run build` — Type-check (`tsc -b`) and build production bundle (`vite build`). Always run after edits.
- `npm run generate:api` — Regenerate Orval React Query hooks & models in `src/api/generated/` from `http://localhost:5000/swagger/v1/swagger.json`.

## Architecture & Conventions
- `src/api/generated/` & `src/api/mutator.ts`: Orval-generated TanStack Query v5 hooks. Prefer these over hand-rolled fetch calls.
- `src/services/`: Thin API wrapper modules (`projectsApi.ts`, `syncApi.ts`, `workItemsApi.ts`, etc.).
- `src/contexts/`: `AuthContext` (with `PersonaSwitcher` for dev), `ProjectContext`, `SignalRContext`, `ThemeContext`.
- `src/components/sync/`: `ExternalConfigCard.tsx` (supports `AzureDevOps`, `Jira`, `GitHub`), `SyncLogFeed.tsx`, `ConflictResolver.tsx`.
- `src/types/index.ts`: Shared TypeScript types (`ExternalSourceType = 'None' | 'AzureDevOps' | 'Jira' | 'GitHub'`).
