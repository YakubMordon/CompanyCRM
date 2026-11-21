import React, { useRef, useState, useEffect } from 'react';
import Typography, { TypographyProps } from '@mui/material/Typography';
import Tooltip, { TooltipProps } from '@mui/material/Tooltip';

export interface TruncatedTextProps extends TypographyProps {
  text: string;
  tooltipTitle?: React.ReactNode;
  tooltipProps?: Omit<TooltipProps, 'children' | 'title'>;
}

export const TruncatedText: React.FC<TruncatedTextProps> = ({
  text,
  tooltipTitle,
  tooltipProps,
  sx,
  ...typographyProps
}) => {
  return (
    <Tooltip title={tooltipTitle || text} arrow {...tooltipProps}>
      <Typography
        noWrap
        sx={{
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          minWidth: 0,
          ...sx,
        }}
        {...typographyProps}
      >
        {text}
      </Typography>
    </Tooltip>
  );
};

export default TruncatedText;
