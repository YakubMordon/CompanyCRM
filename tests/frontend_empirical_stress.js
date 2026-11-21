// tests/frontend_empirical_stress.js
// Real-module empirical verification harness for Milestone 4 frontend source files.
// Compiles and executes actual TypeScript modules under src/companycrm-web/src/ via esbuild.

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const webRoot = path.join(repoRoot, 'src', 'companycrm-web');
const webSrc = path.join(webRoot, 'src');

const webRequire = createRequire(path.join(webRoot, 'package.json'));
const esbuild = webRequire('esbuild');

function loadTsModule(relativeSrcPath) {
  const entryPoint = path.join(webSrc, relativeSrcPath);
  if (!fs.existsSync(entryPoint)) {
    throw new Error(`Source module not found: ${entryPoint}`);
  }

  const result = esbuild.buildSync({
    entryPoints: [entryPoint],
    bundle: true,
    write: false,
    platform: 'node',
    format: 'cjs',
    target: 'node20',
    logLevel: 'silent',
  });

  const code = result.outputFiles[0].text;
  const mod = { exports: {} };
  const wrapper = new Function('exports', 'require', 'module', '__filename', '__dirname', code);
  wrapper(mod.exports, webRequire, mod, entryPoint, path.dirname(entryPoint));
  return mod.exports;
}

console.log('====================================================');
console.log('Milestone 4: Real-Module Frontend Verification Harness');
console.log('====================================================\n');

let passCount = 0;
let failCount = 0;

async function runTest(name, fn) {
  try {
    await fn();
    console.log(`[PASS] ${name}`);
    passCount++;
  } catch (err) {
    console.error(`[FAIL] ${name}:`, err && err.stack ? err.stack : err);
    failCount++;
  }
}

// Load real compiled modules directly from src/companycrm-web/src/
const contextHelpers = loadTsModule('contexts/contextHelpers.ts');
const workItemHelpers = loadTsModule('components/workspace/workItemHelpers.ts');
const syncHelpers = loadTsModule('components/sync/syncHelpers.ts');
const analyticsHelpers = loadTsModule('components/management/analyticsHelpers.ts');
const cutoffHelpers = loadTsModule('components/planning/cutoffHelpers.ts');
const reviewsApiMod = loadTsModule('services/reviewsApi.ts');
const workItemsApiMod = loadTsModule('services/workItemsApi.ts');
const syncApiMod = loadTsModule('services/syncApi.ts');

// -----------------------------------------------------------------------------
// Test 1: contextHelpers — SignalR callback error isolation
// -----------------------------------------------------------------------------
await runTest('contextHelpers.dispatchCallbackSafely: Callback error isolation prevents crash of peer listeners', () => {
  const callbacks = new Set();
  const received = [];
  const errors = [];

  callbacks.add((item) => received.push(`listener1:${item.id}`));
  callbacks.add(() => {
    throw new Error('Explosive callback failure');
  });
  callbacks.add((item) => received.push(`listener3:${item.id}`));

  contextHelpers.dispatchCallbackSafely(
    callbacks,
    { id: 'item-101', title: 'Test Task' },
    (err) => errors.push(err.message)
  );

  assert.equal(received.length, 2);
  assert.equal(received[0], 'listener1:item-101');
  assert.equal(received[1], 'listener3:item-101');
  assert.equal(errors.length, 1);
  assert.equal(errors[0], 'Explosive callback failure');
});

// -----------------------------------------------------------------------------
// Test 2: contextHelpers — Theme mode resolution and toggle
// -----------------------------------------------------------------------------
await runTest('contextHelpers.resolveSavedThemeMode & getNextThemeMode: Valid/corrupted fallback', () => {
  assert.equal(contextHelpers.resolveSavedThemeMode(null), 'light');
  assert.equal(contextHelpers.resolveSavedThemeMode(''), 'light');
  assert.equal(contextHelpers.resolveSavedThemeMode('invalid_neon_purple'), 'light');
  assert.equal(contextHelpers.resolveSavedThemeMode('dark'), 'dark');
  assert.equal(contextHelpers.resolveSavedThemeMode('light'), 'light');
  assert.equal(contextHelpers.getNextThemeMode('light'), 'dark');
  assert.equal(contextHelpers.getNextThemeMode('dark'), 'light');
});

// -----------------------------------------------------------------------------
// Test 3: contextHelpers — Active project resolution & stale ID fallback
// -----------------------------------------------------------------------------
await runTest('contextHelpers.resolveActiveProject: Recovers saved ID and falls back on stale/empty', () => {
  const projectList = [
    { id: 'proj-aaa', name: 'Alpha Project' },
    { id: 'proj-bbb', name: 'Beta Project' },
  ];

  assert.equal(contextHelpers.resolveActiveProject(projectList, null).id, 'proj-aaa');
  assert.equal(contextHelpers.resolveActiveProject(projectList, 'proj-bbb').id, 'proj-bbb');
  assert.equal(contextHelpers.resolveActiveProject(projectList, 'proj-deleted-999').id, 'proj-aaa');
  assert.equal(contextHelpers.resolveActiveProject([], 'proj-bbb'), null);
});

// -----------------------------------------------------------------------------
// Test 4: workItemHelpers — Kanban lock gating & column grouping
// -----------------------------------------------------------------------------
await runTest('workItemHelpers.isWorkItemLockedOnBoard & groupWorkItemsByColumn: Enforces review locks', () => {
  assert.equal(workItemHelpers.isWorkItemLockedOnBoard({ status: 'Backlog', reviewStatus: 'NotRequired', isLockedOnBoard: false }), false);
  assert.equal(workItemHelpers.isWorkItemLockedOnBoard({ status: 'InProgress', reviewStatus: 'Approved', isLockedOnBoard: false }), false);
  assert.equal(workItemHelpers.isWorkItemLockedOnBoard({ status: 'PendingReview', reviewStatus: 'Pending', isLockedOnBoard: false }), true);
  assert.equal(workItemHelpers.isWorkItemLockedOnBoard({ status: 'ClarificationRequested', reviewStatus: 'ClarificationNeeded', isLockedOnBoard: false }), true);
  assert.equal(workItemHelpers.isWorkItemLockedOnBoard({ status: 'Denied', reviewStatus: 'Denied', isLockedOnBoard: false }), true);
  assert.equal(workItemHelpers.isWorkItemLockedOnBoard({ status: 'Todo', reviewStatus: 'Approved', isLockedOnBoard: true }), true);

  const grouped = workItemHelpers.groupWorkItemsByColumn([
    { id: '1', status: 'PendingReview' },
    { id: '2', status: 'Backlog' },
    { id: '3', status: 'Todo' },
    { id: '4', status: 'InProgress' },
    { id: '5', status: 'InVerify' },
    { id: '6', status: 'Done' },
    { id: '7', status: 6 }, // numeric InProgress from SignalR wire fallback
    { id: '8', status: '999' }, // unknown -> falls back to Backlog rather than vanishing
  ]);
  assert.equal(grouped.Backlog.length, 3);
  assert.equal(grouped.Todo.length, 1);
  assert.equal(grouped.InProgress.length, 2);
  assert.equal(grouped.InVerify.length, 1);
  assert.equal(grouped.Done.length, 1);

  const normalizedWi = workItemHelpers.normalizeWorkItemDto({
    id: 'wi-num',
    type: 1,
    status: 6,
    priority: 2,
    reviewStatus: 2,
    lastSyncOrigin: 1,
    createdById: 'emp-9',
    createdByName: 'Maya Patel',
    reviewLatestComment: 'Looks good',
    totalLoggedMinutes: 90,
    billableMinutes: 60,
  });
  assert.equal(normalizedWi.type, 'Bug');
  assert.equal(normalizedWi.status, 'InProgress');
  assert.equal(normalizedWi.priority, 'High');
  assert.equal(normalizedWi.reviewStatus, 'Approved');
  assert.equal(normalizedWi.lastSyncOrigin, 'ExternalWebhook');
  assert.equal(normalizedWi.creatorId, 'emp-9');
  assert.equal(normalizedWi.creatorName, 'Maya Patel');
  assert.equal(normalizedWi.reviewComment, 'Looks good');
  assert.equal(normalizedWi.nonBillableMinutes, 30);
});

// -----------------------------------------------------------------------------
// Test 5: workItemsApi & workItemHelpers — WorkItemHistoryDto canonical action/note
// -----------------------------------------------------------------------------
await runTest('workItemsApi.normalizeWorkItemHistoryDto & workItemHelpers: Maps backend action and note', async () => {
  const wireHistory = {
    id: 'hist-1',
    workItemId: 'wi-1',
    changedById: 'emp-1',
    changedByName: 'David Chen',
    changedAt: '2026-11-18T10:03:32Z',
    action: 'ReviewClarificationRequested',
    fieldName: 'ReviewStatus',
    oldValue: 'Pending',
    newValue: 'ClarificationNeeded',
    note: 'Please attach the stack trace.',
  };

  const normalized = workItemsApiMod.normalizeWorkItemHistoryDto(wireHistory);
  assert.equal(normalized.action, 'ReviewClarificationRequested');
  assert.equal(normalized.changeType, 'ReviewClarificationRequested');
  assert.equal(normalized.note, 'Please attach the stack trace.');
  assert.equal(normalized.notes, 'Please attach the stack trace.');
  assert.equal(workItemHelpers.getHistoryActionLabel(wireHistory), 'ReviewClarificationRequested');
  assert.equal(workItemHelpers.getHistoryNote(wireHistory), 'Please attach the stack trace.');

  // Also test legacy changeType/notes fallback and whitespace-only note
  const legacyHistory = {
    id: 'hist-2',
    changeType: 'StatusChanged',
    notes: '   ',
  };
  assert.equal(workItemHelpers.getHistoryActionLabel(legacyHistory), 'StatusChanged');
  assert.equal(workItemHelpers.getHistoryNote(legacyHistory), null);
});

// -----------------------------------------------------------------------------
// Test 6: reviewsApi — WorkItemReviewQueueDto normalization & ID guards
// -----------------------------------------------------------------------------
await runTest('reviewsApi.normalizeWorkItemReviewQueueDto & route guards: Normalizes wire payloads and rejects invalid IDs', async () => {
  const rawBackendOnly = {
    workItemId: 'd5e4c91c-2222-3333-4444-555566667777',
    workItemCode: 'PRJ-102',
    workItemTitle: 'Bug B',
    createdById: 'creator-1',
    createdByName: 'Alice',
    reviewerId: null,
    reviewerName: null,
    latestComment: 'Please review',
    createdAt: '2026-11-16T12:00:00Z',
  };

  const normalized = reviewsApiMod.normalizeWorkItemReviewQueueDto(rawBackendOnly);
  assert.equal(normalized.id, 'd5e4c91c-2222-3333-4444-555566667777');
  assert.equal(normalized.code, 'PRJ-102');
  assert.equal(normalized.title, 'Bug B');
  assert.equal(normalized.creatorName, 'Alice');
  assert.equal(normalized.reviewerName, 'Unassigned');
  assert.equal(normalized.reviewComment, 'Please review');

  for (const badId of [null, undefined, '', 'undefined']) {
    await assert.rejects(
      () => reviewsApiMod.reviewsApi.approveReview(badId, { startingStatus: 'Backlog' }),
      /Valid review work item ID is required to approve review/
    );
    await assert.rejects(
      () => reviewsApiMod.reviewsApi.requestClarification(badId, { comment: 'Needs info' }),
      /Valid review work item ID is required to request clarification/
    );
    await assert.rejects(
      () => reviewsApiMod.reviewsApi.denyReview(badId, { reason: 'Out of scope' }),
      /Valid review work item ID is required to deny review/
    );
  }
});

// -----------------------------------------------------------------------------
// Test 7: syncApi & syncHelpers — ReconciliationResultDto & SyncEventDto normalization
// -----------------------------------------------------------------------------
await runTest('syncApi.normalizeReconciliationResultDto & syncHelpers: Formats backend reconciliation summary without undefined', () => {
  const wireReconcile = {
    projectId: '4971b67c-0ce1-4c3b-91f9-cad5231c5adc',
    totalItemsChecked: 12,
    updatedCount: 3,
    conflictsCount: 1,
    skippedCount: 8,
    errors: [],
    startedAt: '2026-11-18T13:03:35Z',
    completedAt: '2026-11-18T13:03:36Z',
  };

  const normalized = syncApiMod.normalizeReconciliationResultDto(wireReconcile);
  assert.equal(normalized.totalItemsChecked, 12);
  assert.equal(normalized.conflictsCount, 1);

  const summary = syncHelpers.formatReconciliationSummary(normalized);
  assert.equal(summary.severity, 'success');
  assert.equal(summary.message.includes('undefined'), false);
  assert.equal(summary.message, 'Reconciliation completed: 12 checked, 3 updated, 1 conflicts detected.');

  const errSummary = syncHelpers.formatReconciliationSummary({
    ...normalized,
    errors: ['ADO timeout'],
  });
  assert.equal(errSummary.severity, 'error');
  assert.match(errSummary.message, /ADO timeout/);

  const normalizedEvent = syncHelpers.normalizeSyncEvent({
    projectId: 'p1',
    status: 'WorkItemPushed',
    message: 'Pushed CRM-101',
  });
  assert.equal(normalizedEvent.eventType, 'WorkItemPushed');
  assert.equal(syncHelpers.getSyncChipColor(normalizedEvent.status), 'success');
  assert.equal(syncHelpers.getSyncChipColor('WorkItemImported'), 'success');
  assert.equal(syncHelpers.getSyncChipColor('ConflictDetected'), 'warning');
  assert.equal(syncHelpers.getSyncChipColor('SyncFailed'), 'error');
  assert.equal(syncHelpers.getSyncChipColor('SyncStarted'), 'info');

  assert.equal(syncHelpers.canMergeConflict({ localWorkItemId: null }), false);
  assert.equal(syncHelpers.canMergeConflict({ localWorkItemId: '' }), false);
  assert.equal(syncHelpers.canMergeConflict({ localWorkItemId: 'wi-123' }), true);
});

// -----------------------------------------------------------------------------
// Test 8: analyticsHelpers — Profitability margin & completion chart data
// -----------------------------------------------------------------------------
await runTest('analyticsHelpers.computeEmployeeMargin & buildCompletionChartData: Handles omitted margin, zero/negative revenue, empty counts', () => {
  const profitable = analyticsHelpers.computeEmployeeMargin({
    revenue: 1500,
    grossProfit: 1000,
  });
  assert.equal(profitable.isProfitable, true);
  assert.equal(profitable.marginPercent.toFixed(1), '66.7');

  const negative = analyticsHelpers.computeEmployeeMargin({
    revenue: 500,
    grossProfit: -1500,
  });
  assert.equal(negative.isProfitable, false);
  assert.equal(negative.marginPercent.toFixed(1), '-300.0');

  const zeroRev = analyticsHelpers.computeEmployeeMargin({
    revenue: 0,
    grossProfit: -200,
  });
  assert.equal(zeroRev.isProfitable, false);
  assert.equal(zeroRev.marginPercent.toFixed(1), '0.0');

  const chartData = analyticsHelpers.buildCompletionChartData({
    statusCounts: { Backlog: 3, Todo: 4, InProgress: 2, InVerify: 1, Done: 5 },
  });
  assert.equal(chartData.length, 5);
  assert.equal(chartData.reduce((acc, item) => acc + item.value, 0), 15);

  const emptyChart = analyticsHelpers.buildCompletionChartData({
    statusCounts: {},
  });
  assert.equal(emptyChart.length, 0);
});

// -----------------------------------------------------------------------------
// Test 9: cutoffHelpers — DayOfWeek integer alignment, legacy string parsing, and projectId propagation
// -----------------------------------------------------------------------------
await runTest('cutoffHelpers: Parses backend "[0, 6]" and legacy string arrays to DayOfWeek indices and serializes canonical DTO', () => {
  assert.deepEqual(cutoffHelpers.parseExcludedDaysToIndices({ excludedDaysOfWeekJson: '[0, 6]' }), [0, 6]);
  assert.deepEqual(cutoffHelpers.parseExcludedDaysToIndices({ excludedDaysOfWeekJson: '["Wednesday", "Sunday"]' }), [0, 3]);
  assert.deepEqual(cutoffHelpers.parseExcludedDaysToIndices({ excludedDaysOfWeekJson: '[]' }), []);
  assert.deepEqual(cutoffHelpers.parseExcludedDaysToIndices({ excludedDaysOfWeekJson: 'malformed_json{[' }), [0, 6]);
  assert.deepEqual(cutoffHelpers.parseExcludedDaysToIndices({ excludedDaysOfWeekJson: null }), [0, 6]);
  assert.deepEqual(cutoffHelpers.parseExcludedDaysToIndices({ excludedDaysOfWeek: ['Monday', 5] }), [1, 5]);

  assert.equal(cutoffHelpers.serializeExcludedDays([6, 0, 3, 6, 99]), '[0,3,6]');

  const dto = cutoffHelpers.buildUpdateCutoffConfigDto({
    projectId: 'proj-crm-1',
    cutoffTimeLocal: ' 09:30:00 ',
    timeZoneId: ' America/New_York ',
    isReminderEnabled: true,
    excludedDayIndices: [6, 0, 5],
  });
  assert.equal(dto.projectId, 'proj-crm-1');
  assert.equal(dto.cutoffTimeLocal, '09:30:00');
  assert.equal(dto.timeZoneId, 'America/New_York');
  assert.equal(dto.excludedDaysOfWeekJson, '[0,5,6]');
  assert.deepEqual(dto.excludedDaysOfWeek, [0, 5, 6]);

  assert.equal(cutoffHelpers.resolveTargetCutoffProjectId('global', 'proj-crm-1'), null);
  assert.equal(cutoffHelpers.resolveTargetCutoffProjectId('project', 'proj-crm-1'), 'proj-crm-1');
  assert.equal(cutoffHelpers.resolveTargetCutoffProjectId('project', '   '), null);
  assert.equal(cutoffHelpers.isInheritedGlobalFallback('project', null), true);
  assert.equal(cutoffHelpers.isInheritedGlobalFallback('project', 'proj-crm-1'), false);
  assert.equal(cutoffHelpers.isInheritedGlobalFallback('global', null), false);

  const blankProjDto = cutoffHelpers.buildUpdateCutoffConfigDto({
    projectId: '   ',
    cutoffTimeLocal: '10:00:00',
    timeZoneId: 'UTC',
    isReminderEnabled: true,
    excludedDayIndices: [0, 6],
  });
  assert.equal(blankProjDto.projectId, null);
});

// -----------------------------------------------------------------------------
// Test 10: Validation Schemas & Limits Boundary Enforcement
// -----------------------------------------------------------------------------
await runTest('validation.schemas: Project, WorkItem, and TimeEntry boundary validation', () => {
  const schemasMod = loadTsModule('validation/schemas.ts');
  const { projectSchema, workItemSchema, manualTimeEntrySchema, LIMITS } = schemasMod;

  assert.equal(LIMITS.projectCodeMax, 10);
  assert.equal(LIMITS.projectNameMax, 100);
  assert.equal(LIMITS.workItemTitleMax, 200);

  // Project Code valid boundary
  assert.equal(projectSchema.safeParse({ code: 'PRJ10', name: 'My Project' }).success, true);
  // Project Code too long (> 10)
  assert.equal(projectSchema.safeParse({ code: 'TOOLONGCODE1', name: 'My Project' }).success, false);
  // Project Code non-alphanumeric
  assert.equal(projectSchema.safeParse({ code: 'PRJ-10!', name: 'My Project' }).success, false);

  // Work Item title boundary (200 characters)
  const validTitle = 'A'.repeat(200);
  const invalidTitle = 'A'.repeat(201);
  assert.equal(workItemSchema.safeParse({ title: validTitle }).success, true);
  assert.equal(workItemSchema.safeParse({ title: invalidTitle }).success, false);

  // Manual time entry duration limits (1 to 1440)
  assert.equal(manualTimeEntrySchema.safeParse({ durationMinutes: 60 }).success, true);
  assert.equal(manualTimeEntrySchema.safeParse({ durationMinutes: 0 }).success, false);
  assert.equal(manualTimeEntrySchema.safeParse({ durationMinutes: 1441 }).success, false);
});

// -----------------------------------------------------------------------------
// Test 11: Build artifact sanity check
// -----------------------------------------------------------------------------
await runTest('Frontend production build output exists (src/companycrm-web/dist/index.html)', () => {
  const indexPath = path.join(webRoot, 'dist', 'index.html');
  assert.equal(fs.existsSync(indexPath), true, 'dist/index.html must exist after npm run build');
});

console.log(`\n====================================================`);
console.log(`Test Execution Summary: ${passCount} Passed, ${failCount} Failed`);
console.log(`====================================================`);

if (failCount > 0) {
  process.exit(1);
}
