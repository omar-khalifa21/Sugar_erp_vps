import { Controller, Get, Header, NotFoundException, Res, StreamableFile } from '@nestjs/common';
import { createReadStream } from 'node:fs';
import { lstat, readFile } from 'node:fs/promises';
import { join, resolve, sep } from 'node:path';
import type { Response } from 'express';
import { Public } from '../common/public.decorator';

interface ReleaseManifest {
  channel: 'production';
  version: string;
  filename: string;
  publishedAt: string;
  sha256: string;
  releaseNotes?: string;
  required?: boolean;
  signed?: boolean;
  minimumVersion?: string;
}

@Public()
@Controller('releases/branch-type-1')
export class ReleasesController {
  private readonly root = resolve(process.env.RELEASES_DIR || '/var/lib/sugar-erp/releases');

  private async current(variant: 'desktop' | 'touch' = 'desktop'): Promise<{ manifest: ReleaseManifest; file: string; size: number }> {
    try {
      const directory = resolve(this.root, variant === 'touch' ? 'branch-type-1-touch' : 'branch-type-1');
      const manifest = JSON.parse(await readFile(join(directory, 'current.json'), 'utf8')) as ReleaseManifest;
      if (manifest.channel !== 'production'
        || !/^Sugar-Branch-Type-1-[A-Za-z0-9._-]+\.(exe|msi|msix)$/.test(manifest.filename)
        || !/^[a-f0-9]{64}$/i.test(manifest.sha256)
        || !/^[A-Za-z0-9._-]+$/.test(manifest.version)) throw new Error('Invalid release manifest');
      const file = resolve(directory, manifest.filename);
      if (!file.startsWith(directory + sep)) throw new Error('Invalid release path');
      const details = await lstat(file);
      if (!details.isFile()) throw new Error('Release is not a file');
      return { manifest, file, size: details.size };
    } catch {
      throw new NotFoundException('Branch Type 1 installer is not available');
    }
  }

  @Get('current')
  @Header('Cache-Control', 'no-store')
  async metadata() {
    const { manifest, size } = await this.current();
    return { profile: 'BRANCH_TYPE_1', ...manifest, channel: 'production', required: manifest.required ?? false, signed: manifest.signed === true, size, downloadUrl: 'current/download' };
  }

  @Get('touch/current')
  @Header('Cache-Control', 'no-store')
  async touchMetadata() {
    const { manifest, size } = await this.current('touch');
    return { profile: 'BRANCH_TYPE_1', variant: 'TOUCH', ...manifest, channel: 'production', required: manifest.required ?? false, signed: manifest.signed === true, size, downloadUrl: 'current/download' };
  }

  @Get('current/download')
  @Header('Cache-Control', 'no-store')
  async download(@Res({ passthrough: true }) response: Response): Promise<StreamableFile> {
    return this.stream(response, 'desktop');
  }

  @Get('touch/current/download')
  @Header('Cache-Control', 'no-store')
  async touchDownload(@Res({ passthrough: true }) response: Response): Promise<StreamableFile> {
    return this.stream(response, 'touch');
  }

  private async stream(response: Response, variant: 'desktop' | 'touch'): Promise<StreamableFile> {
    const { manifest, file, size } = await this.current(variant);
    response.setHeader('Content-Type', manifest.filename.endsWith('.msi') ? 'application/x-msi' : 'application/octet-stream');
    response.setHeader('Content-Disposition', `attachment; filename="${manifest.filename}"`);
    response.setHeader('Content-Length', String(size));
    response.setHeader('X-Content-Type-Options', 'nosniff');
    return new StreamableFile(createReadStream(file));
  }
}
