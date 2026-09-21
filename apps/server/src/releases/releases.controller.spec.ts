import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { NotFoundException } from '@nestjs/common';
import { ReleasesController } from './releases.controller';

describe('Branch Type 1 releases', () => {
  let root: string;
  let previousDirectory: string | undefined;

  beforeEach(async () => {
    root = await mkdtemp(join(tmpdir(), 'sugar-releases-'));
    await mkdir(join(root, 'branch-type-1'));
    previousDirectory = process.env.RELEASES_DIR;
    process.env.RELEASES_DIR = root;
  });

  afterEach(async () => {
    if (previousDirectory === undefined) delete process.env.RELEASES_DIR;
    else process.env.RELEASES_DIR = previousDirectory;
    await rm(root, { recursive: true, force: true });
  });

  it('returns only a manifest-listed installer with download headers', async () => {
    const directory = join(root, 'branch-type-1');
    const filename = 'Sugar-Branch-Type-1-1.0.0.exe';
    await writeFile(join(directory, filename), 'installer bytes');
    await writeFile(join(directory, 'current.json'), JSON.stringify({
      channel: 'production', version: '1.0.0', filename, publishedAt: '2026-09-14T00:00:00Z', sha256: 'a'.repeat(64),
    }));
    const controller = new ReleasesController();
    await expect(controller.metadata()).resolves.toMatchObject({ filename, size: 15, profile: 'BRANCH_TYPE_1', required: false, downloadUrl: 'current/download' });
    const headers: Record<string, string> = {};
    const response = { setHeader: (name: string, value: string) => { headers[name] = value; } };
    const download = await controller.download(response as never);
    download.getStream().destroy();
    expect(headers['Content-Disposition']).toBe(`attachment; filename="${filename}"`);
    expect(headers['Content-Length']).toBe('15');
  });

  it('rejects a manifest that names a file outside the release allowlist', async () => {
    await writeFile(join(root, 'branch-type-1', 'current.json'), JSON.stringify({
      channel: 'production', version: '1.0.0', filename: '../secret.txt', publishedAt: '2026-09-14T00:00:00Z', sha256: 'a'.repeat(64),
    }));
    await expect(new ReleasesController().metadata()).rejects.toBeInstanceOf(NotFoundException);
  });

  it('serves the separate touch installer manifest', async () => {
    const directory = join(root, 'branch-type-1-touch');
    await mkdir(directory);
    await writeFile(join(directory, 'Sugar-Branch-Type-1-Touch-0.2.0.exe'), 'touch installer');
    await writeFile(join(directory, 'current.json'), JSON.stringify({
      channel: 'production', version: '0.2.0', filename: 'Sugar-Branch-Type-1-Touch-0.2.0.exe',
      publishedAt: '2026-09-14T00:00:00Z', sha256: 'b'.repeat(64),
    }));
    await expect(new ReleasesController().touchMetadata()).resolves.toMatchObject({
      variant: 'TOUCH', filename: 'Sugar-Branch-Type-1-Touch-0.2.0.exe', size: 15,
    });
  });

  it('rejects a manifest outside the production channel', async () => {
    const directory = join(root, 'branch-type-1');
    const filename = 'Sugar-Branch-Type-1-1.0.0.exe';
    await writeFile(join(directory, filename), 'installer bytes');
    await writeFile(join(directory, 'current.json'), JSON.stringify({
      channel: 'preview', version: '1.0.0', filename, publishedAt: '2026-09-14T00:00:00Z', sha256: 'a'.repeat(64),
    }));
    await expect(new ReleasesController().metadata()).rejects.toBeInstanceOf(NotFoundException);
  });
});
