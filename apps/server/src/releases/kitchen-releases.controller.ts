import { Controller, Get, Header, NotFoundException, Res, StreamableFile } from '@nestjs/common';
import { createReadStream } from 'node:fs';
import { lstat, readFile } from 'node:fs/promises';
import { join, resolve, sep } from 'node:path';
import type { Response } from 'express';
import { Public } from '../common/public.decorator';

@Public()
@Controller('releases/kitchen')
export class KitchenReleasesController {
  private readonly directory = resolve(process.env.RELEASES_DIR || '/var/lib/sugar-erp/releases', 'kitchen');
  private async current() {
    try {
      const manifest = JSON.parse(await readFile(join(this.directory, 'current.json'), 'utf8')) as { channel?: string; filename: string; sha256: string; version: string; required?: boolean; signed?: boolean; [key: string]: unknown };
      if (manifest.channel !== 'production' || !/^Sugar-Kitchen-[A-Za-z0-9._-]+\.exe$/.test(manifest.filename) || !/^[a-f0-9]{64}$/i.test(manifest.sha256) || !/^[A-Za-z0-9._-]+$/.test(manifest.version)) throw new Error();
      const file = resolve(this.directory, manifest.filename);
      if (!file.startsWith(this.directory + sep)) throw new Error();
      const stat = await lstat(file); if (!stat.isFile()) throw new Error();
      return { manifest, file, size: stat.size };
    } catch { throw new NotFoundException('Kitchen installer is not available'); }
  }
  @Get('current') @Header('Cache-Control', 'no-store') async metadata() { const { manifest, size } = await this.current(); return { profile: 'KITCHEN', ...manifest, channel: 'production', required: manifest.required === true, signed: manifest.signed === true, size, downloadUrl: 'current/download' }; }
  @Get('current/download') @Header('Cache-Control', 'no-store') async download(@Res({ passthrough: true }) response: Response) {
    const { manifest, file, size } = await this.current();
    response.setHeader('Content-Type', 'application/octet-stream'); response.setHeader('Content-Disposition', `attachment; filename="${manifest.filename}"`);
    response.setHeader('Content-Length', String(size)); response.setHeader('X-Content-Type-Options', 'nosniff');
    return new StreamableFile(createReadStream(file));
  }
}
