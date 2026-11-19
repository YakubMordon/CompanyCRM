import React from 'react';
import { useDroppable } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import { WorkItemDto, WorkItemStatus } from '../../types';
import { KanbanCard } from './KanbanCard';

interface KanbanColumnProps {
  id: WorkItemStatus;
  title: string;
  items: WorkItemDto[];
  onCardClick: (item: WorkItemDto) => void;
  activeTimerWorkItemId?: string | null;
}

const COLUMN_COLORS: Record<string, string> = {
  Backlog: '#64748b',
  Todo: '#0284c7',
  InProgress: '#d97706',
  InVerify: '#7c3aed',
  Done: '#16a34a',
};

export const KanbanColumn: React.FC<KanbanColumnProps> = ({
  id,
  title,
  items,
  onCardClick,
  activeTimerWorkItemId,
}) => {
  const { setNodeRef, isOver } = useDroppable({
    id,
  });

  const columnColor = COLUMN_COLORS[id] || '#64748b';

  return (
    <Paper
      ref={setNodeRef}
      variant="outlined"
      sx={{
        width: 290,
        minWidth: 290,
        bgcolor: isOver ? 'action.hover' : 'background.paper',
        borderRadius: 2,
        display: 'flex',
        flexDirection: 'column',
        maxHeight: 'calc(100vh - 210px)',
        borderColor: isOver ? 'primary.main' : 'divider',
        borderWidth: isOver ? 2 : 1,
        transition: 'background-color 0.15s ease, border-color 0.15s ease',
      }}
    >
      <Box
        sx={{
          p: 1.5,
          borderBottom: 1,
          borderBottomColor: 'divider',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          borderTop: 3,
          borderTopColor: columnColor,
          borderTopLeftRadius: 8,
          borderTopRightRadius: 8,
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="subtitle2" sx={{ fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.5px' }}>
            {title}
          </Typography>
        </Box>
        <Chip
          label={items.length}
          size="small"
          sx={{
            fontWeight: 700,
            fontSize: '0.75rem',
            height: 22,
            bgcolor: 'action.selected',
          }}
        />
      </Box>

      <Box
        sx={{
          p: 1.5,
          flexGrow: 1,
          overflowY: 'auto',
          minHeight: 180,
        }}
      >
        <SortableContext items={items.map((i) => i.id)} strategy={verticalListSortingStrategy}>
          {items.map((item) => (
            <KanbanCard
              key={item.id}
              item={item}
              onClick={onCardClick}
              isRunningTimer={activeTimerWorkItemId === item.id}
            />
          ))}
        </SortableContext>

        {items.length === 0 && (
          <Box
            sx={{
              height: '100%',
              minHeight: 120,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              border: '2px dashed',
              borderColor: 'divider',
              borderRadius: 2,
            }}
          >
            <Typography variant="caption" color="text.secondary">
              No work items
            </Typography>
          </Box>
        )}
      </Box>
    </Paper>
  );
};
