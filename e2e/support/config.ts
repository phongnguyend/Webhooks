import { config } from 'dotenv';
import path from 'node:path';

config({ path: path.resolve(__dirname, '../.env'), quiet: true });

export function required(name: string, trim = true): string {
  const value = process.env[name];
  if (!value?.trim()) throw new Error(`Set ${name} in e2e/.env or the process environment.`);
  return trim ? value.trim() : value;
}

export const apiURL = process.env.E2E_API_URL || 'http://localhost:5229';
export const uiURL = process.env.E2E_BASE_URL || 'http://localhost:5173';
export type Account = 'USER' | 'ADMIN';
