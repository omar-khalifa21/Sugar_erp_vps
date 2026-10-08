import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { isXlsx } from './xlsx-validation';

describe('XLSX upload validation', () => {
  const fixture = readFileSync(join(__dirname, '../../test/fixtures/minimal-report.xlsx'));
  it('accepts a real synthetic OpenXML workbook', () => expect(isXlsx(fixture)).toBe(true));
  it.each([Buffer.from('PK\x03\x04arbitrary content'), fixture.subarray(0, fixture.length - 5), Buffer.from('not a workbook')])('rejects forged ZIP signatures and truncated content', (content) => {
    expect(isXlsx(content)).toBe(false);
  });
  it('rejects an excessive expansion size', () => {
    const content = Buffer.from(fixture);
    const central = content.indexOf(Buffer.from('PK\x01\x02'));
    content.writeUInt32LE(200_000_000, central + 24);
    expect(isXlsx(content)).toBe(false);
  });
});
