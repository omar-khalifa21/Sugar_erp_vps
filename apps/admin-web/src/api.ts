export type SiteType = 'BRANCH_TYPE_1' | 'BRANCH_TYPE_2' | 'KITCHEN';

export interface Site {
  id: string;
  code: string;
  name: string;
  type: SiteType;
  timezone: string;
  active: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Device {
  id: string;
  siteId: string;
  profile: SiteType;
  enrollmentStatus: 'PENDING' | 'ENROLLED' | 'REVOKED';
  activeWriter: boolean;
  lastSeenAt: string | null;
  appVersion: string | null;
  streamEpoch: number;
  createdAt: string;
}

export interface Item {
  id: string;
  sku: string;
  nameAr: string;
  unit: string;
  quantityScale: number;
  kind: 'PRODUCT' | 'INGREDIENT';
  active: boolean;
  version: number;
  updatedAt: string;
}

export interface Role {
  id: string;
  code: string;
  name: string;
  permissions: string[];
  createdAt: string;
}

export interface User {
  id: string;
  username: string;
  displayName: string;
  active: boolean;
  createdAt: string;
  updatedAt: string;
  siteRoles: Array<{ role: Role; site: Site | null }>;
}

export interface ApiErrorBody {
  code?: string;
  message?: string;
  correlation_id?: string;
  field_errors?: string[];
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly body: ApiErrorBody,
  ) {
    super(body.message || 'تعذر إكمال الطلب');
  }
}

async function request<T>(
  path: string,
  options: RequestInit = {},
  token?: string,
): Promise<T> {
  const response = await fetch(`/api/v1${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
  });

  const body = (await response.json().catch(() => ({}))) as T | ApiErrorBody;
  if (!response.ok) throw new ApiError(response.status, body as ApiErrorBody);
  return body as T;
}

export const api = {
  login: (username: string, password: string) =>
    request<{ access_token: string; token_type: 'Bearer' }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),
  health: () => request<{ status: string; service: string }>('/health'),
  ready: () =>
    request<{ status: string; database: string; migrations: string }>('/ready'),
  sites: (token: string) => request<Site[]>('/sites', {}, token),
  devices: (token: string) => request<Device[]>('/devices', {}, token),
  items: (token: string) => request<Item[]>('/items', {}, token),
  users: (token: string) => request<User[]>('/users', {}, token),
  roles: (token: string) => request<Role[]>('/roles', {}, token),
  createSite: (token: string, input: Pick<Site, 'code' | 'name' | 'type'>) =>
    request<Site>('/sites', { method: 'POST', body: JSON.stringify(input) }, token),
  createItem: (
    token: string,
    input: Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'kind'>,
  ) => request<Item>('/items', { method: 'POST', body: JSON.stringify(input) }, token),
};
