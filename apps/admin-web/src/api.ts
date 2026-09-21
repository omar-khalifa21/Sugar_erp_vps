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
  retailPriceMinor: number;
  kind: 'PRODUCT' | 'INGREDIENT';
  active: boolean;
  version: number;
  updatedAt: string;
}

export interface CafeCustomerInput {
  code: string;
  name: string;
  contact?: string;
  notes?: string;
}

export interface CafeOverview {
  currency: 'EGP';
  totals: { invoiced_minor: string; collected_minor: string; outstanding_minor: string };
  customers: Array<{
    id: string;
    code: string;
    name: string;
    contact: string | null;
    notes: string | null;
    invoiced_minor: string;
    collected_minor: string;
    outstanding_minor: string;
    prices: Array<{
      id: string;
      item_id: string;
      sku: string;
      name_ar: string;
      unit: string;
      retail_price_minor: number;
      price_minor: number;
      version: number;
      updated_at: string;
    }>;
    invoices: Array<{
      id: string;
      number: string;
      issuing_site: { id: string; name: string };
      business_date: string;
      net_minor: string;
      paid_minor: string;
      outstanding_minor: string;
      status: 'OPEN' | 'PARTIALLY_PAID' | 'PAID' | 'REVERSED';
      lines: Array<{
        id: string;
        item_id: string;
        name_ar: string;
        sku: string;
        unit: string;
        quantity_scaled: string;
        quantity_scale: number;
        unit_price_minor: number;
        total_minor: string;
      }>;
    }>;
    payments: Array<{
      id: string;
      reference: string;
      amount_minor: string;
      collected_at_site: { id: string; name: string };
      occurred_at: string;
    }>;
  }>;
}

export interface KitchenOverview {
  sites: Site[];
  stock: Array<{
    id: string;
    site: { id: string; name: string };
    item_id: string;
    name_ar: string;
    sku: string;
    unit: string;
    quantity_scale: number;
    quantity_scaled: string;
    as_of: string;
  }>;
  variances: Array<{
    id: string;
    site: { id: string; name: string };
    item_id: string;
    name_ar: string;
    unit: string;
    quantity_scale: number;
    business_date: string;
    expected_scaled: string;
    actual_scaled: string;
    recorded_waste_scaled: string;
    unexplained_variance_scaled: string;
    cost_minor_per_scale: string | null;
  }>;
  requests: Array<{ id: string; branch: { id: string; name: string }; status: string; submitted_at: string; line_count: number }>;
  shipments: Array<{ id: string; reference: string; branch: { id: string; name: string }; status: string; dispatched_at: string; line_count: number; receipt_status: string | null }>;
}

export interface KitchenRecipe {
  id: string;
  productItemId: string;
  outputScaled: string;
  version: number;
  active: boolean;
  updatedAt: string;
  product: Item;
  components: Array<{ ingredientItemId: string; quantityScaled: string; ingredient: Item }>;
}

export interface QuantityConflict {
  id: string;
  site: { id: string; name: string };
  reference: string;
  status: 'OPEN' | 'PENDING_SITE_APPLY' | 'RESOLVED';
  version: number;
  note: string | null;
  sent_at: string;
  counted_at: string;
  reported_at: string;
  lines: Array<{
    id: string;
    item_id: string;
    name_ar: string;
    unit: string;
    quantity_scale: number;
    sent_scaled: string;
    counted_scaled: string;
    final_scaled: string | null;
    note: string | null;
  }>;
  decision: { reason: string; application_status: 'CREATED' | 'DELIVERED' | 'APPLIED' | 'REJECTED'; decided_at: string; admin_name: string } | null;
}

export interface Role {
  id: string;
  code: string;
  name: string;
  permissions: string[];
  active: boolean;
  createdAt: string;
}

export interface User {
  id: string;
  username: string;
  displayName: string;
  active: boolean;
  status: 'PENDING_PERMISSION' | 'ACTIVE' | 'DISABLED';
  createdAt: string;
  updatedAt: string;
  siteRoles: Array<{ role: Role; site: Site | null }>;
}

export interface BranchOverview {
  site: Site;
  inventory_history: Array<{ id: string; reference_id: string; kind: string; reason: string; user_name: string; occurred_at: string;
    lines: Array<{ item_id: string; name_ar: string; unit: string; quantity_scale: number; location: 'FREEZER' | 'DISPLAY' | 'SALEABLE' | 'HOLD' | 'KITCHEN' | 'TRANSIT'; delta_scaled: string }> }>;
  freshness: { as_of: string | null; stale: boolean };
  stock: Array<{
    id: string;
    item_id: string;
    sku: string;
    name_ar: string;
    kind: Item['kind'];
    unit: string;
    retail_price_minor: number;
    quantity_scale: number;
    location: 'SALEABLE' | 'FREEZER' | 'DISPLAY' | 'KITCHEN' | 'HOLD' | 'TRANSIT';
    quantity_scaled: string;
    version: number;
    as_of: string;
  }>;
  sales: {
    currency: 'EGP';
    today: { net_minor: string; tips_minor: string; receipt_count: number };
    month: { net_minor: string; tips_minor: string; receipt_count: number };
    recent: Array<{ id: string; receipt_number: string; shift_kind: 'MORNING' | 'EVENING'; net_minor: string; tip_minor: string; occurred_at: string }>;
  };
  pending_adjustments: number;
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

async function downloadInstaller(path: string, token: string, onProgress?: (percent: number) => void, signal?: AbortSignal) {
  const response = await fetch(`/api/v1${path}`, { headers: { Authorization: `Bearer ${token}` }, signal });
  if (!response.ok) throw new ApiError(response.status, await response.json().catch(() => ({})));
  const total = Number(response.headers.get('Content-Length'));
  const reader = response.body?.getReader();
  if (!reader) throw new Error('Download stream is unavailable');
  const chunks: Uint8Array<ArrayBuffer>[] = [];
  let received = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    chunks.push(new Uint8Array(value));
    received += value.length;
    onProgress?.(total ? Math.min(100, Math.floor(received * 100 / total)) : 0);
  }
  if (!received || (total && received !== total)) throw new Error('Incomplete installer download');
  const url = URL.createObjectURL(new Blob(chunks, { type: 'application/octet-stream' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = response.headers.get('Content-Disposition')?.match(/filename="([^"]+)"/)?.[1] || 'Sugar-installer.exe';
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export const api = {
  login: (username: string, password: string) =>
    request<{ access_token: string; token_type: 'Bearer'; status: string }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),
  signup: (input: { username: string; displayName: string; password: string }) =>
    request<{ status: 'PENDING_PERMISSION' }>('/auth/signup', { method: 'POST', body: JSON.stringify(input) }),
  branchOneRelease: (token: string) => request<{ channel: 'production'; version: string; filename: string; publishedAt: string; sha256: string; size: number; releaseNotes?: string }>('/releases/branch-type-1/current', {}, token),
  branchOneTouchRelease: (token: string) => request<{ channel: 'production'; version: string; filename: string; publishedAt: string; sha256: string; size: number; releaseNotes?: string }>('/releases/branch-type-1/touch/current', {}, token),
  kitchenRelease: (token: string) => request<{ channel: 'production'; version: string; filename: string; publishedAt: string; sha256: string; size: number; releaseNotes?: string }>('/releases/kitchen/current', {}, token),
  branchTwoRelease: (token: string) => request<{ channel: 'production'; version: string; filename: string; publishedAt: string; sha256: string; size: number; releaseNotes?: string }>('/releases/branch-type-2/current', {}, token),
  downloadBranchOne: (token: string, variant: 'desktop' | 'touch' = 'desktop', onProgress?: (percent: number) => void, signal?: AbortSignal) =>
    downloadInstaller(`/releases/branch-type-1${variant === 'touch' ? '/touch' : ''}/current/download`, token, onProgress, signal),
  downloadKitchen: (token: string, onProgress?: (percent: number) => void, signal?: AbortSignal) =>
    downloadInstaller('/releases/kitchen/current/download', token, onProgress, signal),
  downloadBranchTwo: (token: string, onProgress?: (percent: number) => void, signal?: AbortSignal) =>
    downloadInstaller('/releases/branch-type-2/current/download', token, onProgress, signal),
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
  updateSite: (token: string, id: string, input: Partial<Pick<Site, 'code' | 'name' | 'type' | 'active'>>) =>
    request<Site>(`/sites/${id}`, { method: 'PATCH', body: JSON.stringify(input) }, token),
  archiveSite: (token: string, id: string) => request<Site>(`/sites/${id}`, { method: 'DELETE' }, token),
  createItem: (
    token: string,
    input: Pick<Item, 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind'>,
  ) => request<Item>('/items', { method: 'POST', body: JSON.stringify(input) }, token),
  updateItem: (
    token: string,
    id: string,
    input: Partial<Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind' | 'active'>>,
  ) => request<Item>(`/items/${id}`, { method: 'PATCH', body: JSON.stringify(input) }, token),
  archiveItem: (token: string, id: string) => request<Item>(`/items/${id}`, { method: 'DELETE' }, token),
  createUser: (token: string, input: { username: string; displayName: string; password: string; roleId: string; siteId?: string }) =>
    request<User>('/users', { method: 'POST', body: JSON.stringify(input) }, token),
  updateUser: (token: string, id: string, input: { username?: string; displayName?: string; password?: string; active?: boolean; roleId?: string; siteId?: string }) =>
    request<User>(`/users/${id}`, { method: 'PATCH', body: JSON.stringify(input) }, token),
  archiveUser: (token: string, id: string) => request<User>(`/users/${id}`, { method: 'DELETE' }, token),
  createRole: (token: string, input: { code: string; name: string; permissions: string[] }) =>
    request<Role>('/roles', { method: 'POST', body: JSON.stringify(input) }, token),
  updateRole: (token: string, id: string, input: { code?: string; name?: string; permissions?: string[]; active?: boolean }) =>
    request<Role>(`/roles/${id}`, { method: 'PATCH', body: JSON.stringify(input) }, token),
  archiveRole: (token: string, id: string) => request<Role>(`/roles/${id}`, { method: 'DELETE' }, token),
  issueEnrollmentToken: (token: string, siteId: string) =>
    request<{ id: string; token: string; siteId: string; profile: SiteType; expiresAt: string }>(
      `/sites/${siteId}/enrollment-tokens`,
      { method: 'POST', body: JSON.stringify({ expiresInMinutes: 30 }) },
      token,
    ),
  branchOverview: (token: string, siteId: string) => request<BranchOverview>(`/admin/sites/${siteId}/overview`, {}, token),
  requestStockAdjustment: (token: string, siteId: string, input: { itemId: string; location: BranchOverview['stock'][number]['location']; deltaScaled: string; reason: string; expectedVersion: number }) =>
    request<{ id: string; status: string; delta_scaled: string; created_at: string }>(
      `/admin/sites/${siteId}/stock-adjustments`,
      { method: 'POST', body: JSON.stringify(input) },
      token,
    ),
  cafeOverview: (token: string) => request<CafeOverview>('/admin/cafe/overview', {}, token),
  createCafeCustomer: (token: string, input: CafeCustomerInput) =>
    request('/admin/cafe/customers', { method: 'POST', body: JSON.stringify(input) }, token),
  updateCafeCustomer: (token: string, id: string, input: Partial<CafeCustomerInput> & { active?: boolean }) =>
    request(`/admin/cafe/customers/${id}`, { method: 'PATCH', body: JSON.stringify(input) }, token),
  archiveCafeCustomer: (token: string, id: string) =>
    request(`/admin/cafe/customers/${id}`, { method: 'DELETE' }, token),
  setCafePrice: (token: string, customerId: string, itemId: string, priceMinor: number) =>
    request(`/admin/cafe/customers/${customerId}/prices/${itemId}`, { method: 'PUT', body: JSON.stringify({ priceMinor }) }, token),
  kitchenOverview: (token: string) => request<KitchenOverview>('/admin/kitchen/overview', {}, token),
  kitchenRecipes: (token: string) => request<KitchenRecipe[]>('/admin/kitchen/recipes', {}, token),
  saveKitchenRecipe: (token: string, productId: string, input: { outputScaled: number; expectedVersion: number; components: Array<{ ingredientItemId: string; quantityScaled: number }> }) =>
    request<KitchenRecipe>(`/admin/kitchen/recipes/${productId}`, { method: 'PUT', body: JSON.stringify(input) }, token),
  conflicts: (token: string) => request<QuantityConflict[]>('/admin/conflicts', {}, token),
  decideConflict: (token: string, id: string, input: { expectedVersion: number; reason: string; lines: Array<{ lineId: string; finalScaled: string }> }) =>
    request(`/admin/conflicts/${id}/decision`, { method: 'POST', body: JSON.stringify(input) }, token),
};

export function friendlyError(error: unknown): string {
  if (!(error instanceof ApiError)) return 'تعذر الاتصال بالخادم. حاول مرة أخرى بعد قليل.';
  const messages: Record<string, string> = {
    UNAUTHENTICATED: 'انتهت الجلسة. سجّل الدخول مرة أخرى.',
    FORBIDDEN: 'ليس لديك صلاحية لتنفيذ هذا الإجراء.',
    VALIDATION_ERROR: 'راجع البيانات المدخلة ثم حاول مرة أخرى.',
    CONFLICT: 'لا يمكن تنفيذ التغيير لأنه يتعارض مع بيانات موجودة.',
    BUSINESS_RULE_VIOLATION: 'لا يمكن تنفيذ هذا الإجراء في الحالة الحالية.',
    STALE_VERSION: 'تغيّرت البيانات منذ فتح الشاشة. حدّث الصفحة وحاول مرة أخرى.',
    CONFLICT_ALREADY_DECIDED: 'اتُخذ قرار لهذا التعارض بالفعل.',
    INTERNAL_ERROR: 'حدث عطل مؤقت. لم نفقد أي بيانات، حاول مرة أخرى.',
  };
  if (error.body.code === 'VALIDATION_ERROR' && error.body.field_errors?.length) {
    return error.body.field_errors.map((field) => field.includes('password') ? 'كلمة المرور الجديدة يجب أن تكون ١٢ حرفاً على الأقل. منح الصلاحيات لا يحتاج تغيير كلمة المرور.' : field).join(' ');
  }
  return messages[error.body.code || ''] || (error.status >= 500 ? messages.INTERNAL_ERROR : 'تعذر إكمال الإجراء. راجع البيانات وحاول مرة أخرى.');
}
