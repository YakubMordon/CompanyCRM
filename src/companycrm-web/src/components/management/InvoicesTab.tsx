import React, { useState, useEffect, useCallback } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import DownloadIcon from '@mui/icons-material/Download';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import VisibilityIcon from '@mui/icons-material/Visibility';
import { invoicesApi, InvoiceDto, InvoiceStatus } from '../../services/invoicesApi';

interface InvoicesTabProps {
  projectId?: string;
}

const STATUS_OPTIONS: InvoiceStatus[] = ['Draft', 'Sent', 'Paid', 'Void'];

const STATUS_COLORS: Record<InvoiceStatus, 'default' | 'info' | 'success' | 'error'> = {
  Draft: 'default',
  Sent: 'info',
  Paid: 'success',
  Void: 'error',
};

const formatCurrency = (val: number, currency = 'USD') =>
  new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    maximumFractionDigits: 2,
  }).format(val);

export const InvoicesTab: React.FC<InvoicesTabProps> = ({ projectId }) => {
  const now = new Date();
  const [filterYear, setFilterYear] = useState<number | ''>('');
  const [filterMonth, setFilterMonth] = useState<number | ''>('');
  const [invoices, setInvoices] = useState<InvoiceDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Generate dialog state
  const [genOpen, setGenOpen] = useState(false);
  const [genYear, setGenYear] = useState<number>(now.getFullYear());
  const [genMonth, setGenMonth] = useState<number>(now.getMonth() + 1);
  const [genTaxRate, setGenTaxRate] = useState<number>(0);
  const [isGenerating, setIsGenerating] = useState(false);

  // Line items detail dialog
  const [selectedInvoice, setSelectedInvoice] = useState<InvoiceDto | null>(null);

  const loadInvoices = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await invoicesApi.getInvoices({
        projectId: projectId || undefined,
        year: filterYear === '' ? undefined : filterYear,
        month: filterMonth === '' ? undefined : filterMonth,
      });
      setInvoices(data);
    } catch (err: any) {
      setError(err?.message || 'Failed to load invoices.');
    } finally {
      setIsLoading(false);
    }
  }, [projectId, filterYear, filterMonth]);

  useEffect(() => {
    loadInvoices();
  }, [loadInvoices]);

  const handleGenerate = async () => {
    setIsGenerating(true);
    setError(null);
    try {
      await invoicesApi.generateMonthly({
        year: genYear,
        month: genMonth,
        projectId: projectId || null,
        taxRatePercent: genTaxRate,
      });
      setGenOpen(false);
      await loadInvoices();
    } catch (err: any) {
      setError(err?.message || 'Failed to generate invoices.');
    } finally {
      setIsGenerating(false);
    }
  };

  const handleStatusChange = async (id: string, status: InvoiceStatus) => {
    setError(null);
    try {
      const updated = await invoicesApi.updateStatus(id, status);
      setInvoices((prev) => prev.map((inv) => (inv.id === id ? updated : inv)));
    } catch (err: any) {
      setError(err?.message || 'Failed to update invoice status.');
    }
  };

  const handleDownloadPdf = async (inv: InvoiceDto) => {
    setError(null);
    try {
      await invoicesApi.downloadPdf(inv.id, inv.invoiceNumber);
    } catch (err: any) {
      setError(err?.message || 'Failed to download invoice PDF.');
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
      {/* Toolbar */}
      <Paper
        variant="outlined"
        sx={{
          p: 2,
          borderRadius: 2,
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 2,
        }}
      >
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'center' }}>
          <FormControl size="small" sx={{ width: 130 }}>
            <InputLabel>Year</InputLabel>
            <Select
              value={filterYear}
              label="Year"
              onChange={(e) => setFilterYear(e.target.value === '' ? '' : Number(e.target.value))}
            >
              <MenuItem value="">All Years</MenuItem>
              {[now.getFullYear() - 1, now.getFullYear(), now.getFullYear() + 1].map((y) => (
                <MenuItem key={y} value={y}>
                  {y}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl size="small" sx={{ width: 140 }}>
            <InputLabel>Month</InputLabel>
            <Select
              value={filterMonth}
              label="Month"
              onChange={(e) => setFilterMonth(e.target.value === '' ? '' : Number(e.target.value))}
            >
              <MenuItem value="">All Months</MenuItem>
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <MenuItem key={m} value={m}>
                  {new Date(2000, m - 1, 1).toLocaleString('en-US', { month: 'long' })}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        </Box>

        <Button
          variant="contained"
          startIcon={<ReceiptLongIcon />}
          onClick={() => setGenOpen(true)}
        >
          Generate Monthly Invoices
        </Button>
      </Paper>

      {error && <Alert severity="error">{error}</Alert>}

      {/* Invoices Table */}
      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 2 }}>
        {isLoading ? (
          <Box sx={{ p: 4, textAlign: 'center' }}>
            <CircularProgress size={28} />
          </Box>
        ) : invoices.length === 0 ? (
          <Box sx={{ p: 4, textAlign: 'center' }}>
            <Typography variant="body2" color="text.secondary">
              No invoices found for the selected filters.
            </Typography>
          </Box>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell sx={{ fontWeight: 700 }}>Invoice #</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Project</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Period</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>Hours</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>Subtotal</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>Tax</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>Total</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Due Date</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Status</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {invoices.map((inv) => (
                <TableRow key={inv.id} hover>
                  <TableCell sx={{ fontWeight: 600 }}>{inv.invoiceNumber}</TableCell>
                  <TableCell>
                    {inv.projectName} ({inv.projectCode})
                  </TableCell>
                  <TableCell>
                    {inv.year}-{String(inv.month).padStart(2, '0')}
                  </TableCell>
                  <TableCell align="right">{inv.totalBillableHours.toFixed(2)}h</TableCell>
                  <TableCell align="right">{formatCurrency(inv.subtotalAmount, inv.currency)}</TableCell>
                  <TableCell align="right">
                    {formatCurrency(inv.taxAmount, inv.currency)} ({inv.taxRatePercent}%)
                  </TableCell>
                  <TableCell align="right" sx={{ fontWeight: 700 }}>
                    {formatCurrency(inv.totalAmount, inv.currency)}
                  </TableCell>
                  <TableCell>{inv.dueDate}</TableCell>
                  <TableCell>
                    <Select
                      size="small"
                      value={inv.status}
                      onChange={(e) => handleStatusChange(inv.id, e.target.value as InvoiceStatus)}
                      renderValue={(val) => (
                        <Chip
                          size="small"
                          label={val}
                          color={STATUS_COLORS[val as InvoiceStatus]}
                        />
                      )}
                      sx={{ minWidth: 110 }}
                    >
                      {STATUS_OPTIONS.map((s) => (
                        <MenuItem key={s} value={s}>
                          {s}
                        </MenuItem>
                      ))}
                    </Select>
                  </TableCell>
                  <TableCell align="right">
                    <Tooltip title="View Line Items">
                      <IconButton size="small" onClick={() => setSelectedInvoice(inv)}>
                        <VisibilityIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title="Download PDF">
                      <IconButton size="small" onClick={() => handleDownloadPdf(inv)}>
                        <DownloadIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </TableContainer>

      {/* Generate Monthly Invoices Dialog */}
      <Dialog open={genOpen} onClose={() => setGenOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Generate Monthly Invoices</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <TextField
            label="Year"
            type="number"
            size="small"
            value={genYear}
            onChange={(e) => setGenYear(Number(e.target.value))}
            fullWidth
            margin="dense"
          />
          <FormControl size="small" fullWidth>
            <InputLabel>Month</InputLabel>
            <Select
              value={genMonth}
              label="Month"
              onChange={(e) => setGenMonth(Number(e.target.value))}
            >
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <MenuItem key={m} value={m}>
                  {new Date(2000, m - 1, 1).toLocaleString('en-US', { month: 'long' })}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <TextField
            label="Tax Rate (%)"
            type="number"
            size="small"
            inputProps={{ min: 0, max: 100, step: 0.5 }}
            value={genTaxRate}
            onChange={(e) => setGenTaxRate(Number(e.target.value))}
            fullWidth
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setGenOpen(false)} disabled={isGenerating}>
            Cancel
          </Button>
          <Button variant="contained" onClick={handleGenerate} disabled={isGenerating}>
            {isGenerating ? 'Generating...' : 'Generate'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Line Items Detail Dialog */}
      <Dialog
        open={Boolean(selectedInvoice)}
        onClose={() => setSelectedInvoice(null)}
        maxWidth="md"
        fullWidth
      >
        <DialogTitle>
          {selectedInvoice?.invoiceNumber} — {selectedInvoice?.projectName}
        </DialogTitle>
        <DialogContent>
          {selectedInvoice && (
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell sx={{ fontWeight: 700 }}>Team Member</TableCell>
                  <TableCell sx={{ fontWeight: 700 }}>Description</TableCell>
                  <TableCell align="right" sx={{ fontWeight: 700 }}>Hours</TableCell>
                  <TableCell align="right" sx={{ fontWeight: 700 }}>Rate</TableCell>
                  <TableCell align="right" sx={{ fontWeight: 700 }}>Amount</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {selectedInvoice.lineItems.map((li) => (
                  <TableRow key={li.id}>
                    <TableCell>{li.employeeName}</TableCell>
                    <TableCell>{li.description}</TableCell>
                    <TableCell align="right">{li.hours.toFixed(2)}</TableCell>
                    <TableCell align="right">
                      {formatCurrency(li.hourlyRate, selectedInvoice.currency)}
                    </TableCell>
                    <TableCell align="right">
                      {formatCurrency(li.amount, selectedInvoice.currency)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setSelectedInvoice(null)}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
