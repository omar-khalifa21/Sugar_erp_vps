import { createHash } from 'node:crypto';
import { ReportsService } from './reports.service';

const device = { id: '10000000-0000-4000-8000-000000000001', siteId: '20000000-0000-4000-8000-000000000002' };
const shiftId = '30000000-0000-4000-8000-000000000003';
const content = Buffer.from([0x50, 0x4b, 0x03, 0x04, 1, 2, 3, 4]);
const contentHash = createHash('sha256').update(content).digest('hex');
const input = {
  shiftId,
  reportVersion: 1,
  businessDate: '2026-09-21',
  shiftKind: 'MORNING' as const,
  filename: 'Main_Branch_2026-09-21_morning.xlsx',
  contentHash,
  byteLength: content.length,
  contentBase64: content.toString('base64'),
};

function createPrisma(existing: Record<string, unknown> | null = null) {
  const saved = {
    id: '40000000-0000-4000-8000-000000000004',
    uploadedAt: new Date('2026-09-21T08:00:00.000Z'),
    ...existing,
  };
  let createInput: { data: Record<string, unknown> } | undefined;
  const transaction = {
    $queryRaw: jest.fn().mockResolvedValue([]),
    shiftReport: {
      findUnique: jest.fn().mockResolvedValue(existing ? saved : null),
      create: jest.fn((value: { data: Record<string, unknown> }) => {
        createInput = value;
        return Promise.resolve(saved);
      }),
    },
  };
  const prisma = {
    site: { findUniqueOrThrow: jest.fn().mockResolvedValue({ name: 'Main Branch' }) },
    syncEvent: { findFirst: jest.fn().mockResolvedValue({ id: '50000000-0000-4000-8000-000000000005', payload: { shift_id: shiftId, business_date: '2026-09-21', kind: 'MORNING' } }) },
    $transaction: jest.fn((action: (value: typeof transaction) => unknown) => Promise.resolve(action(transaction))),
  };
  return { prisma, transaction, getCreateInput: () => createInput };
}

describe('ReportsService', () => {
  it('stores a verified production XLSX once', async () => {
    const { prisma, transaction, getCreateInput } = createPrisma();
    const service = new ReportsService(prisma as never);

    await expect(service.upload(device, input)).resolves.toMatchObject({ status: 'accepted' });
    expect(transaction.shiftReport.create).toHaveBeenCalledTimes(1);
    expect(getCreateInput()?.data).toMatchObject({
      shiftId,
      siteId: device.siteId,
      deviceId: device.id,
      filename: input.filename,
      contentHash,
      content,
    });
  });

  it('returns duplicate for an identical retry without writing another row', async () => {
    const { prisma, transaction } = createPrisma({
      shiftId,
      siteId: device.siteId,
      deviceId: device.id,
      reportVersion: 1,
      contentHash,
      byteLength: BigInt(content.length),
      filename: input.filename,
    });
    const service = new ReportsService(prisma as never);

    await expect(service.upload(device, input)).resolves.toMatchObject({ status: 'duplicate' });
    expect(transaction.shiftReport.create).not.toHaveBeenCalled();
  });

  it('rejects a changed retry to preserve historical immutability', async () => {
    const { prisma } = createPrisma({
      shiftId,
      siteId: device.siteId,
      deviceId: device.id,
      reportVersion: 1,
      contentHash: 'f'.repeat(64),
      byteLength: BigInt(content.length),
      filename: input.filename,
    });
    const service = new ReportsService(prisma as never);

    await expect(service.upload(device, input)).rejects.toMatchObject({ response: { code: 'REPORT_IMMUTABILITY_CONFLICT' } });
  });

  it('rejects corrupt content before database storage', async () => {
    const { prisma, transaction } = createPrisma();
    const service = new ReportsService(prisma as never);

    await expect(service.upload(device, { ...input, contentHash: '0'.repeat(64) })).rejects.toMatchObject({ response: { code: 'REPORT_CONTENT_INVALID' } });
    expect(transaction.shiftReport.create).not.toHaveBeenCalled();
  });
});
