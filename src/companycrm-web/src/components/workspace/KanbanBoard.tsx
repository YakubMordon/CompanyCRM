import React, { useState } from 'react';
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  DragStartEvent,
  DragEndEvent,
  closestCorners,
} from '@dnd-kit/core';
import Box from '@mui/material/Box';
import Snackbar from '@mui/material/Snackbar';
import Alert from '@mui/material/Alert';
import { WorkItemDto, WorkItemStatus } from '../../types';
import { KanbanColumn } from './KanbanColumn';
import { KanbanCard } from './KanbanCard';
import { groupWorkItemsByColumn, isWorkItemLockedOnBoard } from './workItemHelpers';

interface KanbanBoardProps {
  workItems: WorkItemDto[];
  onTransitionStatus: (id: string, targetStatus: WorkItemStatus, newIndex?: number) => Promise<void>;
  onCardClick: (item: WorkItemDto) => void;
  activeTimerWorkItemId?: string | null;
}

const COLUMNS: { id: WorkItemStatus; title: string }[] = [
  { id: 'Backlog', title: 'Backlog' },
  { id: 'Todo', title: 'Todo' },
  { id: 'InProgress', title: 'In Progress' },
  { id: 'InVerify', title: 'In Verify' },
  { id: 'Done', title: 'Done' },
];

export const KanbanBoard: React.FC<KanbanBoardProps> = ({
  workItems,
  onTransitionStatus,
  onCardClick,
  activeTimerWorkItemId,
}) => {
  const [activeItem, setActiveItem] = useState<WorkItemDto | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 5,
      },
    })
  );

  const handleDragStart = (event: DragStartEvent) => {
    const item = workItems.find((w) => w.id === event.active.id);
    if (item) {
      setActiveItem(item);
    }
  };

  const handleDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveItem(null);

    if (!over) return;

    const activeId = active.id as string;
    const item = workItems.find((w) => w.id === activeId);
    if (!item) return;

    // Status gating check: if item is locked, block drag
    const isLocked = isWorkItemLockedOnBoard(item);

    if (isLocked) {
      setErrorMessage(
        'Cannot move work item on Kanban board: Review is pending, needs clarification, or is denied.'
      );
      return;
    }

    // Determine target column
    let targetStatus: WorkItemStatus | null = null;

    if (COLUMNS.some((col) => col.id === over.id)) {
      targetStatus = over.id as WorkItemStatus;
    } else {
      // Dropped over another card
      const overItem = workItems.find((w) => w.id === over.id);
      if (overItem) {
        targetStatus = overItem.status;
      }
    }

    if (targetStatus && targetStatus !== item.status) {
      try {
        await onTransitionStatus(activeId, targetStatus);
      } catch (err: any) {
        const errorDetail =
          err?.response?.data?.detail || err?.message || 'Failed to update work item status.';
        setErrorMessage(errorDetail);
      }
    }
  };

  // Group items by column
  // Note: Items with Draft, PendingReview, ClarificationRequested, Denied belong in Backlog column view
  const itemsByColumn = groupWorkItemsByColumn(workItems);

  return (
    <>
      <DndContext
        sensors={sensors}
        collisionDetection={closestCorners}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
      >
        <Box
          sx={{
            display: 'flex',
            gap: 2,
            overflowX: 'auto',
            pb: 2,
            minHeight: 'calc(100vh - 200px)',
          }}
        >
          {COLUMNS.map((col) => (
            <KanbanColumn
              key={col.id}
              id={col.id}
              title={col.title}
              items={itemsByColumn[col.id] || []}
              onCardClick={onCardClick}
              activeTimerWorkItemId={activeTimerWorkItemId}
            />
          ))}
        </Box>

        <DragOverlay>
          {activeItem ? (
            <KanbanCard
              item={activeItem}
              onClick={() => {}}
              isRunningTimer={activeTimerWorkItemId === activeItem.id}
            />
          ) : null}
        </DragOverlay>
      </DndContext>

      <Snackbar
        open={Boolean(errorMessage)}
        autoHideDuration={4000}
        onClose={() => setErrorMessage(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert onClose={() => setErrorMessage(null)} severity="error" sx={{ width: '100%' }}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </>
  );
};
