import type { Config } from 'jest';

const config: Config = {
  rootDir: '..',
  moduleFileExtensions: ['js', 'json', 'ts'],
  testRegex: '.*\\.integration-spec\\.ts$',
  transform: { '^.+\\.(t|j)s$': 'ts-jest' },
  testEnvironment: 'node',
  testTimeout: 30_000,
};

export default config;
