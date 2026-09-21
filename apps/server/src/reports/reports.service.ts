import { HttpException, Injectable, NotFoundException } from '@nestjs/common';
import { Prisma, ShiftKind } from '@prisma/client';
import { createHash } from 'node:crypto';
import { PrismaService } from '../prisma/prisma.service';
import { ReportUploadDto } from './report-upload.dto';

@Injectable()
export class ReportsService {
  constructor(private readonly prisma: PrismaService) {}

  async upload(device: { id: string; siteId: string }, input: ReportUploadDto) {
    const content = Buffer.from(input.contentBase64, 'base64');
    const computedHash = createHash('sha256').update(content).digest('hex');
    if (content.length !== input.byteLength || computedHash !== input.contentHash || !isXlsx(content)) {
      throw reportError(422, 'REPORT_CONTENT_INVALID', 'The report bytes, length, or SHA-256 hash are invalid', false);
    }

    const site = await this.prisma.site.findUniqueOrThrow({ where: { id: device.siteId }, select: { name: true } });
    const expectedFilename = `${sanitizeFileNamePart(site.name)}_${input.businessDate}_${input.shiftKind === 'MORNING' ? 'morning' : 'evening'}.xlsx`;
    const legacyFilename = new RegExp(`^shift-${input.businessDate}-${input.shiftKind === 'MORNING' ? 'morning' : 'evening'}-[0-9a-f]{8}-v${input.reportVersion}\\.xlsx$`, 'i');
    if (input.filename !== expectedFilename && !legacyFilename.test(input.filename)) {
      throw reportError(422, 'REPORT_FILENAME_INVALID', 'The report filename does not match the production naming rule', false);
    }

    const shiftEvent = await this.prisma.syncEvent.findFirst({
      where: {
        deviceId: device.id,
        siteId: device.siteId,
        eventType: 'shift.closed',
        payload: { path: ['shift_id'], equals: input.shiftId },
      },
      select: { id: true, payload: true },
    });
    const payload = shiftEvent?.payload as Record<string, unknown> | undefined;
    if (!shiftEvent || !payload) {
      throw reportError(409, 'REPORT_SHIFT_NOT_SYNCED', 'The shift close must synchronize before its report', true);
    }
    if (payload.business_date !== input.businessDate || payload.kind !== input.shiftKind) {
      throw reportError(409, 'REPORT_SHIFT_MISMATCH', 'The report does not match the synchronized shift close', false);
    }

    return this.prisma.$transaction(async (transaction) => {
      await transaction.$queryRaw(
        Prisma.sql`SELECT "id" FROM "sync_events" WHERE "id" = ${shiftEvent.id}::uuid FOR UPDATE`,
      );
      const existing = await transaction.shiftReport.findUnique({
        where: { shiftId_reportVersion: { shiftId: input.shiftId, reportVersion: input.reportVersion } },
      });
      if (existing) {
        if (
          existing.siteId !== device.siteId ||
          existing.deviceId !== device.id ||
          existing.contentHash !== input.contentHash ||
          existing.byteLength !== BigInt(input.byteLength) ||
          existing.filename !== input.filename
        ) {
          throw reportError(409, 'REPORT_IMMUTABILITY_CONFLICT', 'A different immutable report already exists for this shift version', false);
        }
        return { status: 'duplicate' as const, id: existing.id, uploaded_at: existing.uploadedAt.toISOString() };
      }
      const saved = await transaction.shiftReport.create({
        data: {
          shiftId: input.shiftId,
          siteId: device.siteId,
          deviceId: device.id,
          reportVersion: input.reportVersion,
          businessDate: new Date(`${input.businessDate}T00:00:00.000Z`),
          shiftKind: input.shiftKind as ShiftKind,
          filename: input.filename,
          contentHash: input.contentHash,
          byteLength: BigInt(input.byteLength),
          content,
        },
      });
      return { status: 'accepted' as const, id: saved.id, uploaded_at: saved.uploadedAt.toISOString() };
    });
  }

  async list() {
    const reports = await this.prisma.shiftReport.findMany({
      orderBy: [{ businessDate: 'desc' }, { uploadedAt: 'desc' }],
      take: 250,
      include: { site: { select: { id: true, name: true } } },
    });
    return reports.map((report) => ({
      id: report.id,
      shift_id: report.shiftId,
      site: report.site,
      report_version: report.reportVersion,
      business_date: report.businessDate.toISOString().slice(0, 10),
      shift_kind: report.shiftKind,
      filename: report.filename,
      sha256: report.contentHash,
      size: report.byteLength.toString(),
      uploaded_at: report.uploadedAt.toISOString(),
    }));
  }

  async download(id: string) {
    const report = await this.prisma.shiftReport.findUnique({ where: { id } });
    if (!report) throw new NotFoundException('Shift report not found');
    return report;
  }
}

function isXlsx(content: Buffer): boolean {
  return content.length >= 4 && content[0] === 0x50 && content[1] === 0x4b && content[2] === 0x03 && content[3] === 0x04;
}

function sanitizeFileNamePart(value: string): string {
  const sanitized = [...value.trim()]
    .map((character) => /[<>:"/\\|?*\s]/.test(character) || character.charCodeAt(0) < 32 ? '_' : character)
    .join('')
    .replace(/_+/g, '_')
    .replace(/^[_.]+|[_.]+$/g, '')
    .slice(0, 80)
    .replace(/[_.]+$/g, '');
  return sanitized || 'branch';
}

function reportError(status: number, code: string, message: string, retryable: boolean): HttpException {
  return new HttpException({ code, message, retryable }, status);
}
