import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Avatar from '@mui/material/Avatar';
import Tooltip from '@mui/material/Tooltip';
import LockIcon from '@mui/icons-material/Lock';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { WorkItemDto } from '../../types';
import { StatusChip } from '../common/StatusChip';

interface KanbanCardProps {
  item: WorkItemDto;
  onClick: (item: WorkItemDto) => void;
  isRunningTimer?: boolean;
}

export const KanbanCard: React.FC<KanbanCardProps> = ({ item, onClick, isRunningTimer = false }) => {
  const isLocked =
    item.isLockedOnBoard ||
    item.status === 'PendingReview' ||
    item.status === 'ClarificationRequested' ||
    item.status === 'Denied' ||
    item.reviewStatus === 'Pending' ||
    item.reviewStatus === 'ClarificationNeeded' ||
    item.reviewStatus === 'Denied';

  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: item.id,
    disabled: isLocked,
    data: { item },
  });

  const style: React.CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
    cursor: isLocked ? 'pointer' : 'grab',
  };

  return (
    <Card
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...(!isLocked ? listeners : {})}
      onClick={() => onClick(item)}
      variant="outlined"
      sx={{
        mb: 1.5,
        borderRadius: 2,
        borderColor: isRunningTimer ? 'success.main' : isLocked ? 'warning.main' : 'divider',
        borderWidth: isRunningTimer ? 2 : 1,
        boxShadow: isDragging ? 4 : 1,
        transition: 'all 0.2s ease-in-out',
        '&:hover': {
          borderColor: 'primary.main',
          boxShadow: 2,
        },
      }}
    >
      <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
            <Typography variant="caption" sx={{ fontWeight: 700, color: 'text.secondary' }}>
              {item.code}
            </Typography>
            <StatusChip type={item.type} size="small" />
          </Box>
          <StatusChip priority={item.priority} size="small" />
        </Box>

        <Typography
          variant="body2"
          sx={{
            fontWeight: 600,
            mb: 1,
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
            lineHeight: 1.3,
          }}
        >
          {item.title}
        </Typography>

        {/* Lock or Status Alert Badge */}
        {isLocked && (
          <Box sx={{ mb: 1 }}>
            <Chip
              icon={<LockIcon sx={{ fontSize: '14px !important' }} />}
              label={
                item.status === 'PendingReview'
                  ? `Pending Review (${item.reviewerName || 'Reviewer'})`
                  : item.status === 'ClarificationRequested'
                  ? 'Clarification Needed'
                  : item.status === 'Denied'
                  ? 'Review Denied'
                  : 'Locked'
              }
              color={item.status === 'Denied' ? 'error' : 'warning'}
              size="small"
              sx={{ fontWeight: 600, fontSize: '0.68rem', height: 22 }}
            />
          </Box>
        )}

        {/* Running Timer Badge */}
        {isRunningTimer && (
          <Box sx={{ mb: 1 }}>
            <Chip
              icon={<PlayCircleIcon sx={{ fontSize: '14px !important' }} />}
              label="Timer Running"
              color="success"
              size="small"
              sx={{ fontWeight: 600, fontSize: '0.68rem', height: 22 }}
            />
          </Box>
        )}

        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mt: 1 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            {item.storyPoints != null && (
              <Chip
                label={`${item.storyPoints} SP`}
                size="small"
                variant="outlined"
                sx={{ height: 20, fontSize: '0.65rem', fontWeight: 600 }}
              />
            )}
            {item.externalId && (
              <Tooltip title={`Linked to Azure DevOps: ${item.externalId}`}>
                <Chip
                  icon={<OpenInNewIcon sx={{ fontSize: '10px !important' }} />}
                  label={item.externalId}
                  size="small"
                  color="info"
                  variant="outlined"
                  sx={{ height: 20, fontSize: '0.65rem' }}
                />
              </Tooltip>
            )}
          </Box>

          {item.assigneeName ? (
            <Tooltip title={`Assignee: ${item.assigneeName}`}>
              <Avatar
                sx={{
                  width: 24,
                  height: 24,
                  fontSize: 11,
                  fontWeight: 600,
                  bgcolor: 'primary.dark',
                }}
              >
                {item.assigneeName.charAt(0)}
              </Avatar>
            </Tooltip>
          ) : (
            <Typography variant="caption" color="text.secondary">
              Unassigned
            </Typography>
          )}
        </Box>
      </CardContent>
    </Card>
  );
};
