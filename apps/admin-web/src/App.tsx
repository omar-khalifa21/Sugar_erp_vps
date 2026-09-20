import {
  AlertTriangle,
  Boxes,
  ChefHat,
  ChevronLeft,
  CircleDollarSign,
  ClipboardCheck,
  Cloud,
  DatabaseBackup,
  Download,
  FileSpreadsheet,
  LayoutDashboard,
  LogOut,
  Menu,
  MonitorSmartphone,
  PackageSearch,
  RefreshCw,
  ShieldCheck,
  Smartphone,
  Store,
  UsersRound,
  X,
  type LucideIcon,
} from 'lucide-react';
import { FormEvent, useCallback, useEffect, useState } from 'react';
import {
  ApiError,
  api,
  friendlyError,
  type BranchOverview,
  type CafeCustomerInput,
  type CafeOverview,
  type Device,
  type Item,
  type KitchenOverview,
  type KitchenRecipe,
  type QuantityConflict,
  type Role,
  type Site,
  type SiteType,
  type User,
} from './api';

type PageKey =
  | 'dashboard'
  | 'branches'
  | 'cafe'
  | 'kitchen'
  | 'conflicts'
  | 'catalog'
  | 'team'
  | 'operations';

interface AdminData {
  sites: Site[];
  devices: Device[];
  items: Item[];
  users: User[];
  roles: Role[];
}

interface NavItem {
  key: PageKey;
  label: string;
  icon: LucideIcon;
}

const emptyData: AdminData = { sites: [], devices: [], items: [], users: [], roles: [] };
const navItems: NavItem[] = [
  { key: 'dashboard', label: 'نظرة عامة', icon: LayoutDashboard },
  { key: 'branches', label: 'الفروع والمخزون', icon: Store },
  { key: 'cafe', label: 'حسابات الكافيه', icon: CircleDollarSign },
  { key: 'kitchen', label: 'المطبخ والخامات', icon: ChefHat },
  { key: 'conflicts', label: 'التعارضات والقرارات', icon: ClipboardCheck },
  { key: 'catalog', label: 'الأصناف والتسعير', icon: Boxes },
  { key: 'team', label: 'المستخدمون والصلاحيات', icon: UsersRound },
  { key: 'operations', label: 'التشغيل والنسخ', icon: Cloud },
];

const pageTitles: Record<PageKey, { title: string; subtitle: string }> = {
  dashboard: { title: 'مركز التحكم', subtitle: 'صورة موحّدة للفروع والمطبخ والمزامنة' },
  branches: { title: 'الفروع والمخزون', subtitle: 'المواقع المسجلة وحالة بيانات كل فرع' },
  cafe: { title: 'حسابات الكافيه', subtitle: 'الفواتير والتحصيل والمتبقي دون ازدواج الإيراد' },
  kitchen: { title: 'المطبخ والخامات', subtitle: 'الخامات والاستهلاك والهالك وفروق الجرد' },
  conflicts: { title: 'التعارضات والقرارات', subtitle: 'المراجعة ثم متابعة التطبيق على جهاز الموقع' },
  catalog: { title: 'الأصناف والتسعير', subtitle: 'كتالوج مركزي بإصدارات واضحة' },
  team: { title: 'المستخدمون والصلاحيات', subtitle: 'حسابات مسمّاة وصلاحيات حسب الموقع' },
  operations: { title: 'التشغيل والنسخ الاحتياطي', subtitle: 'الأجهزة والتقارير والإصدارات وحماية البيانات' },
};

const siteTypeLabels: Record<SiteType, string> = {
  BRANCH_TYPE_1: 'فرع نوع ١',
  BRANCH_TYPE_2: 'فرع نوع ٢',
  KITCHEN: 'المطبخ',
};

const permissionOptions = [
  { code: '*', label: 'كل صلاحيات النظام' },
  { code: 'sales.create', label: 'إنشاء المبيعات' },
  { code: 'sales.current_shift.read', label: 'عرض الوردية الحالية' },
  { code: 'sales.history.read', label: 'عرض سجل المبيعات' },
  { code: 'stock.read', label: 'عرض المخزون' },
  { code: 'stock.adjust.request', label: 'طلب تعديل المخزون' },
  { code: 'kitchen.request', label: 'طلب منتجات من المطبخ' },
  { code: 'kitchen.dispatch', label: 'اعتماد وإرسال الطلبات' },
  { code: 'ingredients.read', label: 'عرض خامات المطبخ' },
  { code: 'reports.read', label: 'عرض وتحميل التقارير' },
  { code: 'cafe.invoice', label: 'إنشاء فواتير الكافيه' },
  { code: 'cafe.payment', label: 'تسجيل تحصيلات الكافيه' },
  { code: 'prices.manage', label: 'تعديل الأسعار' },
  { code: 'users.manage', label: 'إدارة المستخدمين والصلاحيات' },
] as const;

const permissionLabel = (code: string) => permissionOptions.find((permission) => permission.code === code)?.label ?? code;

function App() {
  const [token, setToken] = useState(() => sessionStorage.getItem('sugar_admin_token'));
  const [status, setStatus] = useState(() => sessionStorage.getItem('sugar_admin_status'));

  const signIn = (nextToken: string, nextStatus: string) => {
    sessionStorage.setItem('sugar_admin_token', nextToken);
    sessionStorage.setItem('sugar_admin_status', nextStatus);
    setToken(nextToken);
    setStatus(nextStatus);
  };

  const signOut = () => {
    sessionStorage.removeItem('sugar_admin_token');
    sessionStorage.removeItem('sugar_admin_status');
    setToken(null);
    setStatus(null);
  };

  return token && status === 'PENDING_PERMISSION'
    ? <main className="login-shell"><section className="login-panel"><div className="login-card"><h2>بانتظار الصلاحيات</h2><p>تم إنشاء حسابك. يستطيع المسؤول منحك الصلاحيات؛ سجّل الدخول مجدداً بعد الموافقة.</p><button className="primary-button" onClick={signOut}>العودة لتسجيل الدخول</button></div></section></main>
    : token ? <AdminApp token={token} onSignOut={signOut} /> : <LoginScreen onSignIn={signIn} />;
}

function LoginScreen({ onSignIn }: { onSignIn: (token: string, status: string) => void }) {
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [signingUp, setSigningUp] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      if (signingUp) {
        await api.signup({ username, displayName, password });
        setNotice('تم إنشاء الحساب. سجّل الدخول وانتظر موافقة المسؤول.');
        setSigningUp(false);
        setPassword('');
      } else {
        const result = await api.login(username, password);
        onSignIn(result.access_token, result.status);
      }
    } catch (caught) {
      setError(caught instanceof ApiError && caught.status === 401 ? 'اسم المستخدم أو كلمة المرور غير صحيحة.' : friendlyError(caught));
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login-shell">
      <section className="login-brand" aria-label="Sugar ERP">
        <div className="brand-mark large">S</div>
        <div>
          <p className="eyebrow">SUGAR ERP</p>
          <h1>كل عملياتك.<br />في مكان واحد.</h1>
          <p className="login-intro">إدارة مركزية للفروع والمطبخ والمخزون، مع سجل محفوظ ومزامنة واضحة.</p>
          <div className="public-installers" aria-label="تنزيل تطبيقات Sugar ERP">
            <strong>تنزيل تطبيق Windows الصحيح</strong>
            <div>
              <a href="/api/v1/releases/branch-type-1/current/download"><Download size={16} /> فرع نوع ١ · مكتبي</a>
              <a href="/api/v1/releases/branch-type-1/touch/current/download"><Download size={16} /> فرع نوع ١ · لمس</a>
              <a href="/api/v1/releases/branch-type-2/current/download"><Download size={16} /> فرع نوع ٢</a>
              <a href="/api/v1/releases/kitchen/current/download"><Download size={16} /> المطبخ</a>
            </div>
            <small>الإصدار الحالي 1.0.0 · Windows x64</small>
          </div>
        </div>
        <div className="login-status"><ShieldCheck size={20} /> اتصال HTTPS مشفّر بخادم Sugar ERP</div>
      </section>
      <section className="login-panel">
        <form className="login-card" onSubmit={submit}>
          <div className="mobile-brand"><div className="brand-mark">S</div><strong>Sugar ERP</strong></div>
          <p className="eyebrow dark">لوحة الإدارة</p>
          <h2>{signingUp ? 'إنشاء حساب' : 'تسجيل الدخول'}</h2>
          <p className="muted">{signingUp ? 'يبدأ الحساب بلا صلاحيات حتى يوافق المسؤول.' : 'استخدم حسابك للوصول إلى بيانات الشركة.'}</p>
          {signingUp && <label>الاسم<input value={displayName} onChange={(event) => setDisplayName(event.target.value)} required /></label>}
          <label>
            اسم المستخدم
            <input value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="username" required />
          </label>
          <label>
            كلمة المرور
            <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete={signingUp ? 'new-password' : 'current-password'} minLength={signingUp ? 12 : 8} required />
          </label>
          {error && <div className="form-error" role="alert"><AlertTriangle size={18} /> {error}</div>}
          {notice && <div role="status">{notice}</div>}
          <button className="primary-button login-button" disabled={busy}>
            {busy ? <><RefreshCw className="spin" size={19} /> جارٍ التنفيذ</> : <>{signingUp ? 'إنشاء حساب' : 'دخول آمن'} <ChevronLeft size={19} /></>}
          </button>
          <button type="button" className="secondary-button" onClick={() => { setSigningUp(!signingUp); setError(''); setNotice(''); setPassword(''); }}>{signingUp ? 'لديك حساب؟ سجّل الدخول' : 'ليس لديك حساب؟ أنشئ حساباً'}</button>
          {import.meta.env.DEV && (
            <div className="demo-note">
              <strong>بيانات العرض المحلي</strong>
              <span>admin</span>
              <span>SugarAdmin2026!</span>
            </div>
          )}
        </form>
      </section>
    </main>
  );
}

function AdminApp({ token, onSignOut }: { token: string; onSignOut: () => void }) {
  const [page, setPage] = useState<PageKey>('dashboard');
  const [data, setData] = useState<AdminData>(emptyData);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [menuOpen, setMenuOpen] = useState(false);
  const [touchMode, setTouchMode] = useState(() => localStorage.getItem('sugar_touch_mode') === 'true');
  const [lastLoaded, setLastLoaded] = useState<Date | null>(null);
  const [selectedSiteId, setSelectedSiteId] = useState<string | null>(null);
  const [toast, setToast] = useState('');

  const loadData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [sites, devices, items, users, roles] = await Promise.all([
        api.sites(token),
        api.devices(token),
        api.items(token),
        api.users(token),
        api.roles(token),
      ]);
      setData({ sites, devices, items, users, roles });
      setLastLoaded(new Date());
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 401) {
        onSignOut();
        return;
      }
      setError(friendlyError(caught));
    } finally {
      setLoading(false);
    }
  }, [onSignOut, token]);

  useEffect(() => { void loadData(); }, [loadData]);
  useEffect(() => {
    document.body.classList.toggle('touch-mode', touchMode);
    localStorage.setItem('sugar_touch_mode', String(touchMode));
  }, [touchMode]);
  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(''), 3500);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const navigate = (key: PageKey, siteId?: string) => {
    setPage(key);
    setSelectedSiteId(siteId ?? null);
    setMenuOpen(false);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const createSite = async (input: Pick<Site, 'code' | 'name' | 'type'>) => {
    const site = await api.createSite(token, input);
    setData((current) => ({ ...current, sites: [...current.sites, site].sort((a, b) => a.code.localeCompare(b.code)) }));
    setToast('تمت إضافة الموقع بنجاح');
  };

  const updateSite = async (id: string, input: Partial<Pick<Site, 'code' | 'name' | 'type' | 'active'>>) => {
    const site = await api.updateSite(token, id, input);
    setData((current) => ({ ...current, sites: current.sites.map((entry) => entry.id === id ? site : entry) }));
    setToast('تم حفظ تعديلات الموقع');
  };

  const archiveSite = async (id: string) => {
    const site = await api.archiveSite(token, id);
    setData((current) => ({ ...current, sites: current.sites.map((entry) => entry.id === id ? site : entry) }));
    setToast('تمت أرشفة الموقع مع الاحتفاظ بسجله');
  };

  const createItem = async (input: Pick<Item, 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind'>) => {
    const item = await api.createItem(token, input);
    setData((current) => ({ ...current, items: [...current.items, item].sort((a, b) => a.nameAr.localeCompare(b.nameAr, 'ar')) }));
    setToast('تمت إضافة الصنف بنجاح');
  };

  const updateItem = async (id: string, input: Partial<Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind' | 'active'>>) => {
    const item = await api.updateItem(token, id, input);
    setData((current) => ({ ...current, items: current.items.map((entry) => entry.id === id ? item : entry) }));
    setToast('تم حفظ تعديلات الصنف');
  };

  const archiveItem = async (id: string) => {
    const item = await api.archiveItem(token, id);
    setData((current) => ({ ...current, items: current.items.map((entry) => entry.id === id ? item : entry) }));
    setToast('تمت أرشفة الصنف مع الاحتفاظ بالحركات السابقة');
  };

  const createUser = async (input: { username: string; displayName: string; password: string; roleId: string; siteId?: string }) => {
    const user = await api.createUser(token, input);
    setData((current) => ({ ...current, users: [...current.users, user].sort((a, b) => a.username.localeCompare(b.username)) }));
    setToast('تمت إضافة المستخدم');
  };

  const updateUser = async (id: string, input: { username?: string; displayName?: string; password?: string; active?: boolean; roleId?: string; siteId?: string }) => {
    const user = await api.updateUser(token, id, input);
    setData((current) => ({ ...current, users: current.users.map((entry) => entry.id === id ? user : entry) }));
    setToast('تم حفظ تعديلات المستخدم');
  };

  const archiveUser = async (id: string) => {
    const user = await api.archiveUser(token, id);
    setData((current) => ({ ...current, users: current.users.map((entry) => entry.id === id ? user : entry) }));
    setToast('تم إيقاف المستخدم مع الاحتفاظ بسجله');
  };

  const createRole = async (input: { code: string; name: string; permissions: string[] }) => {
    const role = await api.createRole(token, input);
    setData((current) => ({ ...current, roles: [...current.roles, role].sort((a, b) => a.code.localeCompare(b.code)) }));
    setToast('تمت إضافة الدور');
  };

  const updateRole = async (id: string, input: { code?: string; name?: string; permissions?: string[]; active?: boolean }) => {
    const role = await api.updateRole(token, id, input);
    setData((current) => ({ ...current, roles: current.roles.map((entry) => entry.id === id ? role : entry) }));
    setToast('تم حفظ تعديلات الدور');
  };

  const archiveRole = async (id: string) => {
    const role = await api.archiveRole(token, id);
    setData((current) => ({ ...current, roles: current.roles.map((entry) => entry.id === id ? role : entry) }));
    setToast('تمت أرشفة الدور');
  };

  const issueEnrollmentToken = async (siteId: string) => {
    const result = await api.issueEnrollmentToken(token, siteId);
    setToast('تم إنشاء رمز تسجيل صالح لمدة ٣٠ دقيقة');
    return result;
  };
  const loadBranchOverview = useCallback((siteId: string) => api.branchOverview(token, siteId), [token]);
  const requestStockAdjustment = async (siteId: string, input: { itemId: string; location: BranchOverview['stock'][number]['location']; deltaScaled: string; reason: string; expectedVersion: number }) => {
    const result = await api.requestStockAdjustment(token, siteId, input);
    setToast('تم إرسال طلب التعديل إلى جهاز الفرع للتطبيق الموثق');
    return result;
  };
  const loadCafeOverview = useCallback(() => api.cafeOverview(token), [token]);
  const createCafeCustomer = async (input: CafeCustomerInput) => {
    await api.createCafeCustomer(token, input);
    setToast('تمت إضافة حساب الكافيه');
  };
  const updateCafeCustomer = async (id: string, input: Partial<CafeCustomerInput> & { active?: boolean }) => {
    await api.updateCafeCustomer(token, id, input);
    setToast('تم حفظ بيانات الكافيه');
  };
  const archiveCafeCustomer = async (id: string) => {
    await api.archiveCafeCustomer(token, id);
    setToast('تمت أرشفة حساب الكافيه مع الاحتفاظ بفواتيره');
  };
  const setCafePrice = async (customerId: string, itemId: string, priceMinor: number) => {
    await api.setCafePrice(token, customerId, itemId, priceMinor);
    setToast('تم نشر سعر الكافيه الجديد دون تغيير الفواتير القديمة');
  };
  const loadKitchenOverview = useCallback(() => api.kitchenOverview(token), [token]);
  const loadConflicts = useCallback(() => api.conflicts(token), [token]);
  const decideConflict = async (id: string, input: { expectedVersion: number; reason: string; lines: Array<{ lineId: string; finalScaled: string }> }) => {
    await api.decideConflict(token, id, input);
    setToast('تم حفظ القرار وإرساله للموقع للتطبيق');
  };

  return (
    <div className="admin-shell">
      <aside className={`sidebar ${menuOpen ? 'open' : ''}`}>
        <div className="sidebar-head">
          <div className="brand-mark">S</div>
          <div><strong>Sugar ERP</strong><span>لوحة الإدارة</span></div>
          <button className="icon-button close-menu" onClick={() => setMenuOpen(false)} aria-label="إغلاق القائمة"><X /></button>
        </div>
        <nav aria-label="التنقل الرئيسي">
          <p className="nav-label">مساحة العمل</p>
          {navItems.map((item) => {
            const Icon = item.icon;
            return <button key={item.key} className={page === item.key ? 'active' : ''} onClick={() => navigate(item.key)}><Icon size={20} /><span>{item.label}</span></button>;
          })}
        </nav>
        <div className="sidebar-foot">
          <div className="profile-avatar">م</div>
          <div><strong>مدير النظام</strong><span>صلاحية كاملة</span></div>
          <button className="icon-button" onClick={onSignOut} aria-label="تسجيل الخروج"><LogOut size={19} /></button>
        </div>
      </aside>
      {menuOpen && <button className="menu-backdrop" onClick={() => setMenuOpen(false)} aria-label="إغلاق القائمة" />}
      <div className="workspace">
        <header className="topbar">
          <button className="icon-button mobile-menu" onClick={() => setMenuOpen(true)} aria-label="فتح القائمة"><Menu /></button>
          <div className="page-heading"><h1>{pageTitles[page].title}</h1><p>{pageTitles[page].subtitle}</p></div>
          <div className="topbar-actions">
            <button className={`touch-toggle ${touchMode ? 'active' : ''}`} onClick={() => setTouchMode((current) => !current)} aria-pressed={touchMode}>
              <Smartphone size={18} /><span>وضع اللمس</span>
            </button>
            <button className="icon-button" onClick={() => void loadData()} aria-label="تحديث البيانات" title="تحديث البيانات"><RefreshCw size={19} className={loading ? 'spin' : ''} /></button>
          </div>
        </header>
        <main className="content">
          {error && <div className="global-error" role="alert"><AlertTriangle size={20} /><span>{error}</span><button onClick={() => void loadData()}>إعادة المحاولة</button></div>}
          {toast && <div className="toast" role="status"><ClipboardCheck size={19} />{toast}</div>}
          {loading && !lastLoaded ? <LoadingPage /> : (
            <PageContent
              page={page}
              data={data}
              lastLoaded={lastLoaded}
              onCreateSite={createSite}
              onUpdateSite={updateSite}
              onArchiveSite={archiveSite}
              onCreateItem={createItem}
              onUpdateItem={updateItem}
              onArchiveItem={archiveItem}
              onCreateUser={createUser}
              onUpdateUser={updateUser}
              onArchiveUser={archiveUser}
              onCreateRole={createRole}
              onUpdateRole={updateRole}
              onArchiveRole={archiveRole}
              onIssueEnrollmentToken={issueEnrollmentToken}
              onLoadBranchOverview={loadBranchOverview}
              onRequestStockAdjustment={requestStockAdjustment}
              onLoadCafeOverview={loadCafeOverview}
              onCreateCafeCustomer={createCafeCustomer}
              onUpdateCafeCustomer={updateCafeCustomer}
              onArchiveCafeCustomer={archiveCafeCustomer}
              onSetCafePrice={setCafePrice}
              onLoadKitchenOverview={loadKitchenOverview}
              onLoadConflicts={loadConflicts}
              onDecideConflict={decideConflict}
              onNavigate={navigate}
              selectedSiteId={selectedSiteId}
              onSelectSite={setSelectedSiteId}
              token={token}
            />
          )}
        </main>
      </div>
    </div>
  );
}

function PageContent({ page, data, lastLoaded, onCreateSite, onUpdateSite, onArchiveSite, onCreateItem, onUpdateItem, onArchiveItem, onCreateUser, onUpdateUser, onArchiveUser, onCreateRole, onUpdateRole, onArchiveRole, onIssueEnrollmentToken, onLoadBranchOverview, onRequestStockAdjustment, onLoadCafeOverview, onCreateCafeCustomer, onUpdateCafeCustomer, onArchiveCafeCustomer, onSetCafePrice, onLoadKitchenOverview, onLoadConflicts, onDecideConflict, onNavigate, selectedSiteId, onSelectSite, token }: {
  token: string;
  page: PageKey;
  data: AdminData;
  lastLoaded: Date | null;
  onCreateSite: (input: Pick<Site, 'code' | 'name' | 'type'>) => Promise<void>;
  onUpdateSite: (id: string, input: Partial<Pick<Site, 'code' | 'name' | 'type' | 'active'>>) => Promise<void>;
  onArchiveSite: (id: string) => Promise<void>;
  onCreateItem: (input: Pick<Item, 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind'>) => Promise<void>;
  onUpdateItem: (id: string, input: Partial<Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind' | 'active'>>) => Promise<void>;
  onArchiveItem: (id: string) => Promise<void>;
  onCreateUser: (input: { username: string; displayName: string; password: string; roleId: string; siteId?: string }) => Promise<void>;
  onUpdateUser: (id: string, input: { username?: string; displayName?: string; password?: string; active?: boolean; roleId?: string; siteId?: string }) => Promise<void>;
  onArchiveUser: (id: string) => Promise<void>;
  onCreateRole: (input: { code: string; name: string; permissions: string[] }) => Promise<void>;
  onUpdateRole: (id: string, input: { code?: string; name?: string; permissions?: string[]; active?: boolean }) => Promise<void>;
  onArchiveRole: (id: string) => Promise<void>;
  onIssueEnrollmentToken: (siteId: string) => Promise<{ id: string; token: string; siteId: string; profile: SiteType; expiresAt: string }>;
  onLoadBranchOverview: (siteId: string) => Promise<BranchOverview>;
  onRequestStockAdjustment: (siteId: string, input: { itemId: string; location: BranchOverview['stock'][number]['location']; deltaScaled: string; reason: string; expectedVersion: number }) => Promise<{ id: string; status: string; delta_scaled: string; created_at: string }>;
  onLoadCafeOverview: () => Promise<CafeOverview>;
  onCreateCafeCustomer: (input: CafeCustomerInput) => Promise<void>;
  onUpdateCafeCustomer: (id: string, input: Partial<CafeCustomerInput> & { active?: boolean }) => Promise<void>;
  onArchiveCafeCustomer: (id: string) => Promise<void>;
  onSetCafePrice: (customerId: string, itemId: string, priceMinor: number) => Promise<void>;
  onLoadKitchenOverview: () => Promise<KitchenOverview>;
  onLoadConflicts: () => Promise<QuantityConflict[]>;
  onDecideConflict: (id: string, input: { expectedVersion: number; reason: string; lines: Array<{ lineId: string; finalScaled: string }> }) => Promise<void>;
  onNavigate: (page: PageKey, siteId?: string) => void;
  selectedSiteId: string | null;
  onSelectSite: (siteId: string | null) => void;
}) {
  switch (page) {
    case 'dashboard': return <Dashboard data={data} lastLoaded={lastLoaded} onNavigate={onNavigate} />;
    case 'branches': return <BranchesPage data={data} selectedSiteId={selectedSiteId} onSelectSite={onSelectSite} onCreate={onCreateSite} onUpdate={onUpdateSite} onArchive={onArchiveSite} onLoadOverview={onLoadBranchOverview} onRequestAdjustment={onRequestStockAdjustment} />;
    case 'cafe': return <CafePage items={data.items} onLoad={onLoadCafeOverview} onCreate={onCreateCafeCustomer} onUpdate={onUpdateCafeCustomer} onArchive={onArchiveCafeCustomer} onSetPrice={onSetCafePrice} />;
    case 'kitchen': return <KitchenPage data={data} onLoad={onLoadKitchenOverview} token={token} />;
    case 'conflicts': return <ConflictsPage onLoad={onLoadConflicts} onDecide={onDecideConflict} />;
    case 'catalog': return <CatalogPage items={data.items} onCreate={onCreateItem} onUpdate={onUpdateItem} onArchive={onArchiveItem} />;
    case 'team': return <TeamPage users={data.users} roles={data.roles} sites={data.sites} onCreateUser={onCreateUser} onUpdateUser={onUpdateUser} onArchiveUser={onArchiveUser} onCreateRole={onCreateRole} onUpdateRole={onUpdateRole} onArchiveRole={onArchiveRole} />;
    case 'operations': return <OperationsPage data={data} onIssueEnrollmentToken={onIssueEnrollmentToken} token={token} />;
  }
}

function Dashboard({ data, lastLoaded, onNavigate }: { data: AdminData; lastLoaded: Date | null; onNavigate: (page: PageKey, siteId?: string) => void }) {
  const branches = data.sites.filter((site) => site.active && site.type !== 'KITCHEN');
  const enrolled = data.devices.filter((device) => device.enrollmentStatus === 'ENROLLED').length;
  const freshness = lastLoaded ? new Intl.DateTimeFormat('ar-EG', { hour: '2-digit', minute: '2-digit', timeZone: 'Africa/Cairo' }).format(lastLoaded) : '—';

  return <div className="page-stack cupcake-dashboard">
    <section className="welcome-strip"><div><span className="live-dot" /><div><strong>كل شيء يعمل بهدوء</strong><small>آخر تحديث {freshness}</small></div></div><button onClick={() => onNavigate('operations')}>حالة النظام <ChevronLeft size={17} /></button></section>
    <section className="simple-stats">
      <div><span>الفروع</span><strong>{branches.length}</strong></div>
      <div><span>الأجهزة المتصلة</span><strong>{enrolled}</strong></div>
      <div><span>الأصناف النشطة</span><strong>{data.items.filter((item) => item.active).length}</strong></div>
    </section>
    <section className="panel branch-home">
      <PanelHeading title="الفروع" subtitle="اختر فرعاً لرؤية المخزون والمبيعات والتفاصيل" action="إدارة الكل" onAction={() => onNavigate('branches')} />
      <div className="branch-tiles">
        {branches.map((site) => {
          const connected = data.devices.some((device) => device.siteId === site.id && device.enrollmentStatus === 'ENROLLED');
          return <button key={site.id} onClick={() => onNavigate('branches', site.id)}><span className="cupcake-site-icon"><Store /></span><span><strong>{site.name}</strong><small>{siteTypeLabels[site.type]}</small></span><span className={connected ? 'tiny-state online' : 'tiny-state'}>{connected ? 'متصل' : 'بانتظار الجهاز'}</span><ChevronLeft /></button>;
        })}
        {!branches.length && <EmptyState icon={Store} title="لا توجد فروع بعد" text="أضف أول فرع لتبدأ." />}
      </div>
    </section>
    <section className="quiet-actions">
      <button onClick={() => onNavigate('conflicts')}><ClipboardCheck /><span><strong>التعارضات</strong><small>لا توجد تعارضات مفتوحة</small></span><ChevronLeft /></button>
      <button onClick={() => onNavigate('operations')}><DatabaseBackup /><span><strong>حماية البيانات</strong><small>النسخ والاستعادة والأجهزة</small></span><ChevronLeft /></button>
    </section>
  </div>;
}

function BranchesPage({ data, selectedSiteId, onSelectSite, onCreate, onUpdate, onArchive, onLoadOverview, onRequestAdjustment }: {
  data: AdminData;
  selectedSiteId: string | null;
  onSelectSite: (id: string | null) => void;
  onCreate: (input: Pick<Site, 'code' | 'name' | 'type'>) => Promise<void>;
  onUpdate: (id: string, input: Partial<Pick<Site, 'code' | 'name' | 'type' | 'active'>>) => Promise<void>;
  onArchive: (id: string) => Promise<void>;
  onLoadOverview: (siteId: string) => Promise<BranchOverview>;
  onRequestAdjustment: (siteId: string, input: { itemId: string; location: BranchOverview['stock'][number]['location']; deltaScaled: string; reason: string; expectedVersion: number }) => Promise<unknown>;
}) {
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [type, setType] = useState<SiteType>('BRANCH_TYPE_1');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setMessage('');
    try { await onCreate({ code, name, type }); setCode(''); setName(''); setShowForm(false); }
    catch (caught) { setMessage(friendlyError(caught)); }
    finally { setBusy(false); }
  };

  const selected = data.sites.find((site) => site.id === selectedSiteId);
  if (selected) return <BranchDetail site={selected} data={data} onBack={() => onSelectSite(null)} onUpdate={onUpdate} onArchive={onArchive} onLoadOverview={onLoadOverview} onRequestAdjustment={onRequestAdjustment} />;

  return <div className="page-stack">
    <SectionToolbar count={`${data.sites.length} مواقع`} action="إضافة موقع" onAction={() => setShowForm((value) => !value)} />
    {showForm && <form className="panel inline-form" onSubmit={submit}>
      <div><label>كود الموقع<input value={code} onChange={(event) => setCode(event.target.value)} placeholder="BRANCH-03" required /></label></div>
      <div><label>اسم الموقع<input value={name} onChange={(event) => setName(event.target.value)} placeholder="فرع المعادي" required /></label></div>
      <div><label>نوع الموقع<select value={type} onChange={(event) => setType(event.target.value as SiteType)}><option value="BRANCH_TYPE_1">فرع نوع ١</option><option value="BRANCH_TYPE_2">فرع نوع ٢</option><option value="KITCHEN">مطبخ</option></select></label></div>
      <button className="primary-button" disabled={busy}>{busy ? 'جارٍ الحفظ' : 'حفظ الموقع'}</button>
      {message && <p className="form-error">{message}</p>}
    </form>}
    <section className="branch-card-grid">
      {data.sites.map((site) => <button className={`simple-branch-card ${!site.active ? 'archived' : ''}`} key={site.id} onClick={() => onSelectSite(site.id)}><span className={`site-icon ${site.type === 'KITCHEN' ? 'kitchen' : ''}`}>{site.type === 'KITCHEN' ? <ChefHat /> : <Store />}</span><span><strong>{site.name}</strong><small>{siteTypeLabels[site.type]}</small></span><span className={`pill ${site.active ? 'success' : 'neutral'}`}>{site.active ? 'نشط' : 'مؤرشف'}</span><ChevronLeft /></button>)}
      {!data.sites.length && <EmptyState icon={Store} title="لا توجد مواقع" text="استخدم زر إضافة موقع للبدء." />}
    </section>
  </div>;
}

function BranchDetail({ site, data, onBack, onUpdate, onArchive, onLoadOverview, onRequestAdjustment }: { site: Site; data: AdminData; onBack: () => void; onUpdate: (id: string, input: Partial<Pick<Site, 'code' | 'name' | 'type' | 'active'>>) => Promise<void>; onArchive: (id: string) => Promise<void>; onLoadOverview: (siteId: string) => Promise<BranchOverview>; onRequestAdjustment: (siteId: string, input: { itemId: string; location: BranchOverview['stock'][number]['location']; deltaScaled: string; reason: string; expectedVersion: number }) => Promise<unknown> }) {
  const [editing, setEditing] = useState(false);
  const [confirmArchive, setConfirmArchive] = useState(false);
  const [message, setMessage] = useState('');
  const [form, setForm] = useState({ code: site.code, name: site.name, type: site.type });
  const [overview, setOverview] = useState<BranchOverview | null>(null);
  const [overviewLoading, setOverviewLoading] = useState(true);
  const [adjusting, setAdjusting] = useState<BranchOverview['stock'][number] | null>(null);
  const [adjustment, setAdjustment] = useState({ delta: '', reason: '' });
  const devices = data.devices.filter((device) => device.siteId === site.id);
  const refreshOverview = useCallback(async () => { setOverviewLoading(true); setMessage(''); try { setOverview(await onLoadOverview(site.id)); } catch (caught) { setMessage(friendlyError(caught)); } finally { setOverviewLoading(false); } }, [onLoadOverview, site.id]);
  useEffect(() => { void refreshOverview(); }, [refreshOverview]);
  const submit = async (event: FormEvent) => { event.preventDefault(); setMessage(''); try { await onUpdate(site.id, form); setEditing(false); } catch (caught) { setMessage(friendlyError(caught)); } };
  const archive = async () => { if (!confirmArchive) { setConfirmArchive(true); return; } setMessage(''); try { await onArchive(site.id); setConfirmArchive(false); } catch (caught) { setMessage(friendlyError(caught)); } };
  const submitAdjustment = async (event: FormEvent) => { event.preventDefault(); if (!adjusting) return; const numeric = Number(adjustment.delta); if (!Number.isFinite(numeric) || numeric === 0) { setMessage('أدخل فرق كمية صحيحاً غير الصفر.'); return; } setMessage(''); try { await onRequestAdjustment(site.id, { itemId: adjusting.item_id, location: adjusting.location, deltaScaled: String(Math.round(numeric * adjusting.quantity_scale)), reason: adjustment.reason, expectedVersion: adjusting.version }); setAdjusting(null); setAdjustment({ delta: '', reason: '' }); await refreshOverview(); } catch (caught) { setMessage(friendlyError(caught)); } };
  return <div className="page-stack branch-detail">
    <button className="back-button" onClick={onBack}>→ كل المواقع</button>
    <section className="branch-title"><div className="cupcake-site-icon large"><Store /></div><div><span>{siteTypeLabels[site.type]}</span><h2>{site.name}</h2><p>{site.code}</p></div><div className="branch-title-actions"><button className="secondary-button" onClick={() => setEditing((value) => !value)}>تعديل</button><button className={confirmArchive ? 'danger-button confirm' : 'danger-button'} onClick={() => void archive()}>{confirmArchive ? 'تأكيد الأرشفة' : site.active ? 'أرشفة' : 'مؤرشف'}</button></div></section>
    {message && <div className="form-error" role="alert">{message}</div>}
    {editing && <form className="panel inline-form" onSubmit={submit}><label>الكود<input value={form.code} onChange={(event) => setForm({ ...form, code: event.target.value })} required /></label><label>الاسم<input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required /></label><label>النوع<select value={form.type} disabled={devices.length > 0} onChange={(event) => setForm({ ...form, type: event.target.value as SiteType })}><option value="BRANCH_TYPE_1">فرع نوع ١</option><option value="BRANCH_TYPE_2">فرع نوع ٢</option><option value="KITCHEN">مطبخ</option></select></label><button className="primary-button">حفظ</button></form>}
    {overviewLoading && !overview ? <div className="panel mini-loading"><RefreshCw className="spin" /> جارٍ تحميل بيانات الفرع…</div> : overview && <>
      <section className="simple-stats branch-stats"><div><span>مبيعات اليوم</span><strong>{formatMoney(overview.sales.today.net_minor)}</strong><small>{overview.sales.today.receipt_count} إيصالات · EGP</small></div><div><span>إيراد الشهر</span><strong>{formatMoney(overview.sales.month.net_minor)}</strong><small>{overview.sales.month.receipt_count} إيصالات · EGP</small></div><div><span>الأجهزة</span><strong>{devices.length}</strong><small>{devices.filter((device) => device.enrollmentStatus === 'ENROLLED').length} متصل</small></div></section>
      <section className={`freshness-note ${overview.freshness.stale ? 'stale' : ''}`}><span className="live-dot" /><span>{overview.freshness.as_of ? `المخزون حتى ${formatDate(overview.freshness.as_of)}` : 'لم يصل رصيد مخزون بعد'}</span>{overview.pending_adjustments > 0 && <span className="pill warning">{overview.pending_adjustments} تعديل بانتظار التطبيق</span>}</section>
      {adjusting && <form className="panel adjustment-form" onSubmit={submitAdjustment}><div><strong>طلب تعديل: {adjusting.name_ar}</strong><small>لن تتغير الكمية حتى يطبّق جهاز الفرع الطلب ويسجله.</small></div><label>فرق الكمية<input type="number" step={1 / adjusting.quantity_scale} value={adjustment.delta} onChange={(event) => setAdjustment({ ...adjustment, delta: event.target.value })} placeholder="مثال: -2 أو 3" required /></label><label>سبب التعديل<input value={adjustment.reason} onChange={(event) => setAdjustment({ ...adjustment, reason: event.target.value })} minLength={3} required /></label><button className="primary-button">إرسال الطلب</button><button type="button" className="secondary-button" onClick={() => setAdjusting(null)}>إلغاء</button></form>}
      <section className="panel table-panel"><PanelHeading title="المخزون الحالي" subtitle="الكميات والأسعار حسب الصنف والموقع؛ التعديل يتم كطلب موثق" /><div className="table-wrap"><table><thead><tr><th>الصنف</th><th>سعر البيع</th><th>التصنيف</th><th>الموقع</th><th>الكمية</th><th>آخر مزامنة</th><th>إجراء</th></tr></thead><tbody>{overview.stock.map((row) => <tr key={row.id}><td><strong>{row.name_ar}</strong></td><td><strong className="price-value">{formatMoney(String(row.retail_price_minor))} EGP</strong></td><td>{row.kind === 'PRODUCT' ? 'منتج' : 'خامة'}</td><td>{locationLabel(row.location)}</td><td><strong>{formatQuantity(row.quantity_scaled, row.quantity_scale)} {row.unit}</strong></td><td>{formatDate(row.as_of)}</td><td><div className="row-actions"><button onClick={() => { setAdjusting(row); setAdjustment({ delta: '', reason: '' }); }}>طلب تعديل</button></div></td></tr>)}</tbody></table></div>{!overview.stock.length && <EmptyState icon={Boxes} title="لا يوجد رصيد بعد" text="سيظهر الرصيد بعد أول مزامنة من جهاز الموقع." />}</section>
      <section className="panel table-panel"><PanelHeading title="سجل حركات المخزون" subtitle="آخر ١٠٠ معاملة متزامنة؛ المرجع والمستخدم والسبب محفوظة في الخادم" /><div className="table-wrap"><table><thead><tr><th>الوقت</th><th>الحركة</th><th>الصنف</th><th>الموقع</th><th>فرق الكمية</th><th>المستخدم</th><th>السبب والمرجع</th></tr></thead><tbody>{overview.inventory_history.flatMap((movement) => movement.lines.map((line) => <tr key={`${movement.id}:${line.item_id}:${line.location}`}><td>{formatDate(movement.occurred_at)}</td><td>{movement.kind}</td><td>{line.name_ar}</td><td>{locationLabel(line.location)}</td><td className="ltr">{formatQuantity(line.delta_scaled, line.quantity_scale)} {line.unit}</td><td>{movement.user_name}</td><td>{movement.reason}<small className="cell-note ltr">{movement.reference_id}</small></td></tr>))}</tbody></table></div>{!overview.inventory_history.length && <EmptyState icon={Boxes} title="لا توجد حركات متزامنة" text="تظهر المعاملات بعد المزامنة من جهاز الفرع." />}</section>
      <section className="two-column"><article className="panel table-panel"><PanelHeading title="مبيعات اليوم" subtitle="الإيصالات الفعلية فقط، والإكراميات منفصلة" />{overview.sales.recent.length ? <div className="table-wrap"><table><thead><tr><th>الإيصال</th><th>الوردية</th><th>الصافي</th></tr></thead><tbody>{overview.sales.recent.map((sale) => <tr key={sale.id}><td className="ltr">{sale.receipt_number}</td><td>{sale.shift_kind === 'MORNING' ? 'صباحية' : 'مسائية'}</td><td>{formatMoney(sale.net_minor)} EGP</td></tr>)}</tbody></table></div> : <EmptyState compact icon={CircleDollarSign} title="لا مبيعات متزامنة" text="ستظهر الإيصالات هنا بعد اتصال جهاز الفرع." />}</article><article className="panel"><PanelHeading title="تفاصيل الفرع" subtitle="التشغيل والاتصال" /><dl className="detail-list"><div><dt>المنطقة الزمنية</dt><dd>{site.timezone}</dd></div><div><dt>حالة الموقع</dt><dd>{site.active ? 'نشط' : 'مؤرشف'}</dd></div><div><dt>نوع التشغيل</dt><dd>{siteTypeLabels[site.type]}</dd></div><div><dt>أنواع الأصناف</dt><dd>{new Set(overview.stock.map((row) => row.kind)).size}</dd></div></dl></article></section>
    </>}
  </div>;
}

function CatalogPage({ items, onCreate, onUpdate, onArchive }: {
  items: Item[];
  onCreate: (input: Pick<Item, 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind'>) => Promise<void>;
  onUpdate: (id: string, input: Partial<Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'retailPriceMinor' | 'kind' | 'active'>>) => Promise<void>;
  onArchive: (id: string) => Promise<void>;
}) {
  const blankForm = { nameAr: '', unit: 'قطعة', quantityScale: 1, priceEgp: '', kind: 'PRODUCT' as Item['kind'] };
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [confirmArchiveId, setConfirmArchiveId] = useState<string | null>(null);
  const [form, setForm] = useState(blankForm);
  const [message, setMessage] = useState('');

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setMessage('');
    const retailPriceMinor = moneyInputToMinor(form.priceEgp);
    if (retailPriceMinor === null) {
      setMessage('أدخل سعراً صحيحاً بالجنيه، وبحد أقصى رقمين بعد العلامة.');
      return;
    }
    const input = {
      nameAr: form.nameAr,
      unit: form.unit,
      quantityScale: form.quantityScale,
      retailPriceMinor,
      kind: form.kind,
    };
    try {
      if (editingId) await onUpdate(editingId, input);
      else await onCreate(input);
      setForm(blankForm);
      setEditingId(null);
      setShowForm(false);
    } catch (caught) {
      setMessage(friendlyError(caught));
    }
  };

  const edit = (item: Item) => {
    setForm({
      nameAr: item.nameAr,
      unit: item.unit,
      quantityScale: item.quantityScale,
      priceEgp: moneyMinorToInput(item.retailPriceMinor),
      kind: item.kind,
    });
    setEditingId(item.id);
    setShowForm(true);
  };

  const archive = async (id: string) => {
    if (confirmArchiveId !== id) { setConfirmArchiveId(id); return; }
    setMessage('');
    try { await onArchive(id); setConfirmArchiveId(null); }
    catch (caught) { setMessage(friendlyError(caught)); }
  };

  return <div className="page-stack">
    <SectionToolbar count={`${items.length} أصناف`} action="إضافة صنف وسعره" onAction={() => { setEditingId(null); setForm(blankForm); setShowForm((value) => !value); }} />
    {message && <div className="form-error" role="alert">{message}</div>}
    {showForm && <form className="panel inline-form catalog-form priced-form" onSubmit={submit}>
      <label>الاسم العربي<input value={form.nameAr} onChange={(event) => setForm({ ...form, nameAr: event.target.value })} required /></label>
      <label>سعر البيع (جنيه)<input className="ltr" inputMode="decimal" value={form.priceEgp} onChange={(event) => setForm({ ...form, priceEgp: event.target.value })} placeholder="0.00" required /></label>
      <label>الوحدة<input value={form.unit} onChange={(event) => setForm({ ...form, unit: event.target.value })} required /></label>
      <label>دقة الكمية<input type="number" min="1" step="1" value={form.quantityScale} onChange={(event) => setForm({ ...form, quantityScale: Number(event.target.value) })} required /></label>
      <label>النوع<select value={form.kind} onChange={(event) => setForm({ ...form, kind: event.target.value as Item['kind'] })}><option value="PRODUCT">منتج</option><option value="INGREDIENT">خامة</option></select></label>
      <button className="primary-button">{editingId ? 'حفظ الصنف والسعر' : 'حفظ الصنف'}</button>
      <button type="button" className="secondary-button" onClick={() => { setShowForm(false); setEditingId(null); }}>إلغاء</button>
    </form>}
    <section className="panel table-panel">
      <PanelHeading title="الأصناف والتسعيرات" subtitle="كل سعر بالجنيه المصري؛ أي تغيير جديد لا يمس أسعار الفواتير السابقة" />
      <div className="table-wrap"><table><thead><tr><th>الصنف</th><th>سعر البيع</th><th>النوع</th><th>الوحدة</th><th>الحالة</th><th>إجراءات</th></tr></thead><tbody>
        {items.map((item) => <tr key={item.id}><td><strong>{item.nameAr}</strong></td><td><strong className={item.retailPriceMinor === 0 ? 'price-missing' : 'price-value'}>{item.retailPriceMinor === 0 ? 'غير مسعّر' : `${formatMoney(String(item.retailPriceMinor))} EGP`}</strong></td><td>{item.kind === 'PRODUCT' ? 'منتج' : 'خامة'}</td><td>{item.unit}</td><td><span className={`pill ${item.active ? 'success' : 'neutral'}`}>{item.active ? 'نشط' : 'مؤرشف'}</span></td><td><div className="row-actions"><button onClick={() => edit(item)}>تعديل السعر والبيانات</button><button className={confirmArchiveId === item.id ? 'confirm-delete' : ''} disabled={!item.active} onClick={() => void archive(item.id)}>{confirmArchiveId === item.id ? 'تأكيد' : 'أرشفة'}</button></div></td></tr>)}
      </tbody></table></div>
      {!items.length && <EmptyState icon={PackageSearch} title="الكتالوج فارغ" text="أضف المنتجات والخامات وأسعارها المركزية." />}
    </section>
  </div>;
}

function CafePage({ items, onLoad, onCreate, onUpdate, onArchive, onSetPrice }: {
  items: Item[];
  onLoad: () => Promise<CafeOverview>;
  onCreate: (input: CafeCustomerInput) => Promise<void>;
  onUpdate: (id: string, input: Partial<CafeCustomerInput>) => Promise<void>;
  onArchive: (id: string) => Promise<void>;
  onSetPrice: (customerId: string, itemId: string, priceMinor: number) => Promise<void>;
}) {
  const blankForm: CafeCustomerInput = { code: '', name: '', contact: '', notes: '' };
  const [overview, setOverview] = useState<CafeOverview | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selectedInvoiceId, setSelectedInvoiceId] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(false);
  const [confirmArchive, setConfirmArchive] = useState(false);
  const [form, setForm] = useState<CafeCustomerInput>(blankForm);
  const [priceEdit, setPriceEdit] = useState<{ itemId: string; value: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState('');
  const refresh = useCallback(async () => {
    setLoading(true);
    setMessage('');
    try { setOverview(await onLoad()); }
    catch (caught) { setMessage(friendlyError(caught)); }
    finally { setLoading(false); }
  }, [onLoad]);
  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => { void onLoad().then(setOverview).catch(() => undefined); }, 10_000);
    return () => window.clearInterval(timer);
  }, [refresh, onLoad]);

  const selected = overview?.customers.find((customer) => customer.id === selectedId) ?? null;
  const selectedInvoice = selected?.invoices.find((invoice) => invoice.id === selectedInvoiceId) ?? null;
  const openCreate = () => { setEditing(false); setForm(blankForm); setShowForm(true); };
  const openEdit = () => {
    if (!selected) return;
    setEditing(true);
    setForm({ code: selected.code, name: selected.name, contact: selected.contact ?? '', notes: selected.notes ?? '' });
    setShowForm(true);
  };
  const submitCustomer = async (event: FormEvent) => {
    event.preventDefault();
    setMessage('');
    try {
      if (editing && selected) await onUpdate(selected.id, form);
      else await onCreate(form);
      setShowForm(false);
      setEditing(false);
      await refresh();
    } catch (caught) { setMessage(friendlyError(caught)); }
  };
  const archive = async () => {
    if (!selected) return;
    if (!confirmArchive) { setConfirmArchive(true); return; }
    setMessage('');
    try { await onArchive(selected.id); setSelectedId(null); setConfirmArchive(false); await refresh(); }
    catch (caught) { setMessage(friendlyError(caught)); }
  };
  const savePrice = async (event: FormEvent) => {
    event.preventDefault();
    if (!selected || !priceEdit) return;
    const priceMinor = moneyInputToMinor(priceEdit.value);
    if (priceMinor === null) { setMessage('أدخل سعراً صحيحاً بالجنيه، وبحد أقصى رقمين بعد العلامة.'); return; }
    setMessage('');
    try { await onSetPrice(selected.id, priceEdit.itemId, priceMinor); setPriceEdit(null); await refresh(); }
    catch (caught) { setMessage(friendlyError(caught)); }
  };

  if (selected) return <div className="page-stack cafe-detail">
    <button className="back-button" onClick={() => { setSelectedId(null); setSelectedInvoiceId(null); setShowForm(false); }}>→ كل حسابات الكافيه</button>
    <section className="branch-title cafe-title"><div className="cupcake-site-icon large"><CircleDollarSign /></div><div><span>حساب كافيه</span><h2>{selected.name}</h2><p>{selected.code}{selected.contact ? ` · ${selected.contact}` : ''}</p></div><div className="branch-title-actions"><button className="secondary-button" onClick={openEdit}>تعديل</button><button className={confirmArchive ? 'danger-button confirm' : 'danger-button'} onClick={() => void archive()}>{confirmArchive ? 'تأكيد الأرشفة' : 'أرشفة'}</button></div></section>
    {message && <div className="form-error" role="alert">{message}</div>}
    {showForm && <CafeCustomerForm form={form} setForm={setForm} editing={editing} onSubmit={submitCustomer} onCancel={() => setShowForm(false)} />}
    <section className="simple-stats branch-stats"><div><span>إجمالي ما أخذه</span><strong>{formatMoney(selected.invoiced_minor)}</strong><small>قيمة الفواتير · EGP</small></div><div><span>المحصّل</span><strong>{formatMoney(selected.collected_minor)}</strong><small>دفعات فعلية · EGP</small></div><div><span>لسه عليه</span><strong>{formatMoney(selected.outstanding_minor)}</strong><small>رصيد مستحق · EGP</small></div></section>
    {selected.notes && <section className="customer-note"><strong>ملاحظة الحساب</strong><span>{selected.notes}</span></section>}
    <section className="panel cafe-prices"><PanelHeading title="أسعار هذا الكافيه" subtitle="السعر الخاص يتغلب على سعر البيع الأساسي فقط للفواتير الجديدة" /><div className="price-grid">{items.filter((item) => item.active && item.kind === 'PRODUCT').map((item) => {
      const custom = selected.prices.find((price) => price.item_id === item.id);
      return <article key={item.id}><div><strong>{item.nameAr}</strong><small>{custom ? 'سعر خاص' : 'السعر الأساسي'}</small></div><span>{formatMoney(String(custom?.price_minor ?? item.retailPriceMinor))} EGP</span><button onClick={() => setPriceEdit({ itemId: item.id, value: moneyMinorToInput(custom?.price_minor ?? item.retailPriceMinor) })}>تغيير</button></article>;
    })}</div>{priceEdit && <form className="price-editor" onSubmit={savePrice}><label>السعر الجديد (جنيه)<input className="ltr" autoFocus inputMode="decimal" value={priceEdit.value} onChange={(event) => setPriceEdit({ ...priceEdit, value: event.target.value })} required /></label><button className="primary-button">نشر السعر</button><button type="button" className="secondary-button" onClick={() => setPriceEdit(null)}>إلغاء</button></form>}</section>
    <section className="two-column cafe-ledger"><article className="panel"><PanelHeading title="الحاجات اللي استلمها" subtitle="اضغط على فاتورة لرؤية الأصناف والكميات والأسعار المحفوظة" /><div className="invoice-list">{selected.invoices.map((invoice) => <button key={invoice.id} className={selectedInvoiceId === invoice.id ? 'selected' : ''} onClick={() => setSelectedInvoiceId(selectedInvoiceId === invoice.id ? null : invoice.id)}><span><strong>{invoice.number}</strong><small>{invoice.business_date} · {invoice.issuing_site.name}</small></span><span><strong>{formatMoney(invoice.net_minor)} EGP</strong><small>متبقي {formatMoney(invoice.outstanding_minor)}</small></span><ChevronLeft /></button>)}{!selected.invoices.length && <EmptyState compact icon={Boxes} title="لا توجد فواتير" text="ستظهر البضاعة بعد مزامنة أول فاتورة." />}</div></article><article className="panel"><PanelHeading title="الدفعات" subtitle="المبالغ المحصّلة فعلياً، منفصلة عن الإيراد" /><div className="payment-list">{selected.payments.map((payment) => <div key={payment.id}><span><strong>{formatMoney(payment.amount_minor)} EGP</strong><small>{payment.collected_at_site.name}</small></span><span><strong>{payment.reference}</strong><small>{formatDate(payment.occurred_at)}</small></span></div>)}{!selected.payments.length && <EmptyState compact icon={CircleDollarSign} title="لم يُحصّل شيء بعد" text="الرصيد ما زال مستحقاً." />}</div></article></section>
    {selectedInvoice && <section className="panel table-panel invoice-detail"><PanelHeading title={`تفاصيل الفاتورة ${selectedInvoice.number}`} subtitle="الأسماء والأسعار هنا لقطات تاريخية لا تتغير عند تعديل التسعير" /><div className="table-wrap"><table><thead><tr><th>الصنف</th><th>الكمية</th><th>سعر الوحدة</th><th>الإجمالي</th></tr></thead><tbody>{selectedInvoice.lines.map((line) => <tr key={line.id}><td><strong>{line.name_ar}</strong></td><td>{formatQuantity(line.quantity_scaled, line.quantity_scale)} {line.unit}</td><td>{formatMoney(String(line.unit_price_minor))} EGP</td><td><strong>{formatMoney(line.total_minor)} EGP</strong></td></tr>)}</tbody></table></div></section>}
  </div>;

  return <div className="page-stack">
    <SectionToolbar count={`${overview?.customers.length ?? 0} حسابات`} action="إضافة كافيه" onAction={openCreate} />
    {message && <div className="form-error" role="alert">{message}</div>}
    {showForm && <CafeCustomerForm form={form} setForm={setForm} editing={false} onSubmit={submitCustomer} onCancel={() => setShowForm(false)} />}
    {loading && !overview ? <div className="panel mini-loading"><RefreshCw className="spin" /> جارٍ تحميل حسابات الكافيه…</div> : overview && <>
      <section className="simple-stats branch-stats"><div><span>فواتير الكافيه</span><strong>{formatMoney(overview.totals.invoiced_minor)}</strong><small>إيراد مفوتر · EGP</small></div><div><span>المحصّل</span><strong>{formatMoney(overview.totals.collected_minor)}</strong><small>نقد محصّل · EGP</small></div><div><span>المتبقي</span><strong>{formatMoney(overview.totals.outstanding_minor)}</strong><small>ذمم مدينة · EGP</small></div></section>
      <section className="cafe-tiles">{overview.customers.map((customer) => <button key={customer.id} onClick={() => setSelectedId(customer.id)}><span className="cupcake-site-icon"><CircleDollarSign /></span><span><strong>{customer.name}</strong><small>{customer.invoices.length} فواتير · {customer.prices.length} أسعار خاصة</small></span><span className="balance-glance"><small>المتبقي</small><strong>{formatMoney(customer.outstanding_minor)} EGP</strong></span><ChevronLeft /></button>)}{!overview.customers.length && <EmptyState icon={CircleDollarSign} title="لا توجد حسابات كافيه" text="أضف أول كافيه وحدد أسعاره الخاصة." />}</section>
    </>}
  </div>;
}

function CafeCustomerForm({ form, setForm, editing, onSubmit, onCancel }: { form: CafeCustomerInput; setForm: (value: CafeCustomerInput) => void; editing: boolean; onSubmit: (event: FormEvent) => void; onCancel: () => void }) {
  return <form className="panel inline-form cafe-customer-form" onSubmit={onSubmit}><label>كود الحساب<input className="ltr" value={form.code} onChange={(event) => setForm({ ...form, code: event.target.value })} required /></label><label>اسم الكافيه<input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required /></label><label>بيانات التواصل<input value={form.contact ?? ''} onChange={(event) => setForm({ ...form, contact: event.target.value })} /></label><label>ملاحظات<input value={form.notes ?? ''} onChange={(event) => setForm({ ...form, notes: event.target.value })} /></label><button className="primary-button">{editing ? 'حفظ التعديل' : 'إضافة الحساب'}</button><button type="button" className="secondary-button" onClick={onCancel}>إلغاء</button></form>;
}

function KitchenPage({ data, onLoad, token }: { data: AdminData; onLoad: () => Promise<KitchenOverview>; token: string }) {
  const [overview, setOverview] = useState<KitchenOverview | null>(null);
  const [recipes, setRecipes] = useState<KitchenRecipe[]>([]);
  const [productId, setProductId] = useState('');
  const [output, setOutput] = useState('1');
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  useEffect(() => { let mounted = true; onLoad().then((result) => { if (mounted) setOverview(result); }).catch((caught) => { if (mounted) setMessage(friendlyError(caught)); }); return () => { mounted = false; }; }, [onLoad]);
  useEffect(() => { let mounted = true; api.kitchenRecipes(token).then((result) => { if (mounted) setRecipes(result); }).catch((caught) => { if (mounted) setMessage(friendlyError(caught)); }); return () => { mounted = false; }; }, [token]);
  const ingredients = data.items.filter((item) => item.kind === 'INGREDIENT' && item.active);
  const products = data.items.filter((item) => item.kind === 'PRODUCT' && item.active);
  const chooseProduct = (nextId: string) => {
    setProductId(nextId);
    const existing = recipes.find((recipe) => recipe.productItemId === nextId);
    const product = products.find((item) => item.id === nextId);
    setOutput(existing && product ? String(Number(existing.outputScaled) / product.quantityScale) : '1');
    setAmounts(Object.fromEntries((existing?.components ?? []).map((component) => [component.ingredientItemId, String(Number(component.quantityScaled) / component.ingredient.quantityScale)])));
  };
  const saveRecipe = async (event: FormEvent) => {
    event.preventDefault(); setMessage('');
    const product = products.find((item) => item.id === productId);
    if (!product) return;
    const outputScaled = Number(output) * product.quantityScale;
    const components = ingredients.flatMap((ingredient) => {
      const value = Number(amounts[ingredient.id] || 0) * ingredient.quantityScale;
      return value > 0 ? [{ ingredientItemId: ingredient.id, quantityScaled: value }] : [];
    });
    if (!Number.isSafeInteger(outputScaled) || outputScaled < 1 || !components.length || components.some((line) => !Number.isSafeInteger(line.quantityScaled))) { setMessage('أدخل كميات صحيحة حسب وحدات القياس وأضف خامة واحدة على الأقل.'); return; }
    setSaving(true);
    try {
      const existing = recipes.find((recipe) => recipe.productItemId === productId);
      const saved = await api.saveKitchenRecipe(token, productId, { outputScaled, expectedVersion: existing?.version ?? 0, components });
      setRecipes((current) => [...current.filter((recipe) => recipe.productItemId !== productId), saved]);
      setMessage('تم نشر الوصفة للمطبخ. الشحنات الجديدة ستحفظ نسخة الوصفة وتخصم الخامات تلقائياً.');
    } catch (caught) { setMessage(friendlyError(caught)); } finally { setSaving(false); }
  };
  const reviewCount = overview?.variances.filter((row) => BigInt(row.unexplained_variance_scaled) !== 0n).length ?? 0;
  return <div className="page-stack">
    {message && <div className="form-error" role="alert">{message}</div>}
    <form className="panel role-editor" onSubmit={saveRecipe}><PanelHeading title="وصفات الإنتاج" subtitle="حدد الخامات المطلوبة لكمية إنتاج واحدة؛ المطبخ يخصمها مرة واحدة عند اعتماد الشحنة" /><div className="inline-form role-name-fields"><label>المنتج<select value={productId} onChange={(event) => chooseProduct(event.target.value)} required><option value="">اختر المنتج</option>{products.map((item) => <option key={item.id} value={item.id}>{item.nameAr}</option>)}</select></label><label>كمية الناتج<input className="ltr" inputMode="decimal" min="0.001" value={output} onChange={(event) => setOutput(event.target.value)} required /></label></div>{productId && <fieldset><legend>الخامات لكل كمية ناتج</legend><div className="permission-grid">{ingredients.map((ingredient) => <label key={ingredient.id} className={Number(amounts[ingredient.id] || 0) > 0 ? 'selected' : ''}><span><strong>{ingredient.nameAr}</strong><small>{ingredient.unit}</small></span><input className="ltr quantity-cell" inputMode="decimal" min="0" value={amounts[ingredient.id] ?? ''} onChange={(event) => setAmounts({ ...amounts, [ingredient.id]: event.target.value })} placeholder="0" /></label>)}</div></fieldset>}<div className="form-actions"><button className="primary-button" disabled={saving || !productId}>{saving ? 'جارٍ الحفظ…' : recipes.some((recipe) => recipe.productItemId === productId) ? 'نشر نسخة وصفة جديدة' : 'نشر الوصفة'}</button></div></form>
    <section className="stats-grid compact"><StatCard label="الخامات المسجلة" value={String(ingredients.length)} note="في الكتالوج المركزي" icon={Boxes} tone="orange" /><StatCard label="أرصدة متزامنة" value={String(overview?.stock.length ?? 0)} note="حسب آخر اتصال" icon={ChefHat} tone="indigo" /><StatCard label="فروق تحتاج مراجعة" value={String(reviewCount)} note="غير الهالك المسجل" icon={AlertTriangle} tone="blue" /></section>
    {!overview ? <div className="panel mini-loading"><RefreshCw className="spin" /> جارٍ تحميل بيانات المطبخ…</div> : <><section className="two-column kitchen-columns"><article className="panel table-panel"><PanelHeading title="طلبات الفروع" subtitle="كل الفروع تستخدم نفس مسار الطلب" /><div className="table-wrap"><table><thead><tr><th>الفرع</th><th>الأصناف</th><th>الحالة</th><th>وقت الطلب</th></tr></thead><tbody>{overview.requests.map((row) => <tr key={row.id}><td><strong>{row.branch.name}</strong></td><td>{row.line_count}</td><td><span className="pill warning">{row.status}</span></td><td>{formatDate(row.submitted_at)}</td></tr>)}</tbody></table></div></article><article className="panel table-panel"><PanelHeading title="الشحنات والوارد" subtitle="حالة الإرسال والاستلام من قاعدة الخادم" /><div className="table-wrap"><table><thead><tr><th>المرجع</th><th>الفرع</th><th>الحالة</th><th>وقت الإرسال</th></tr></thead><tbody>{overview.shipments.map((row) => <tr key={row.id}><td><strong>{row.reference}</strong></td><td>{row.branch.name}</td><td><span className={`pill ${row.status === 'CONFLICT' ? 'danger' : row.status === 'RECEIVED' ? 'success' : 'warning'}`}>{row.status}</span></td><td>{formatDate(row.dispatched_at)}</td></tr>)}</tbody></table></div></article></section><section className="two-column kitchen-columns"><article className="panel table-panel"><PanelHeading title="رصيد الخامات" subtitle="الرصيد وآخر وقت مزامنة" /><div className="table-wrap"><table><thead><tr><th>الخامة</th><th>الكمية</th><th>حتى</th></tr></thead><tbody>{overview.stock.map((row) => <tr key={row.id}><td><strong>{row.name_ar}</strong></td><td>{formatQuantity(row.quantity_scaled, row.quantity_scale)} {row.unit}</td><td>{formatDate(row.as_of)}</td></tr>)}</tbody></table></div></article><article className="panel table-panel"><PanelHeading title="الهالك وفروق الجرد" subtitle="الهالك المسجل منفصل دائماً عن النقص غير المفسر" /><div className="table-wrap"><table><thead><tr><th>الخامة</th><th>هالك مسجل</th><th>فرق غير مفسر</th><th>التاريخ</th></tr></thead><tbody>{overview.variances.map((row) => <tr key={row.id}><td><strong>{row.name_ar}</strong></td><td>{formatQuantity(row.recorded_waste_scaled, row.quantity_scale)} {row.unit}</td><td><strong className={BigInt(row.unexplained_variance_scaled) > 0n ? 'price-missing' : ''}>{formatQuantity(row.unexplained_variance_scaled, row.quantity_scale)} {row.unit}</strong></td><td>{row.business_date}</td></tr>)}</tbody></table></div></article></section></>}
  </div>;
}

function ConflictsPage({ onLoad, onDecide }: { onLoad: () => Promise<QuantityConflict[]>; onDecide: (id: string, input: { expectedVersion: number; reason: string; lines: Array<{ lineId: string; finalScaled: string }> }) => Promise<void> }) {
  const [conflicts, setConflicts] = useState<QuantityConflict[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [finals, setFinals] = useState<Record<string, string>>({});
  const [message, setMessage] = useState('');
  const refresh = useCallback(async () => { setMessage(''); try { setConflicts(await onLoad()); } catch (caught) { setMessage(friendlyError(caught)); } }, [onLoad]);
  useEffect(() => { void refresh(); }, [refresh]);
  const selected = conflicts?.find((conflict) => conflict.id === selectedId) ?? null;
  const choose = (conflict: QuantityConflict) => { setSelectedId(conflict.id); setReason(''); setFinals(Object.fromEntries(conflict.lines.map((line) => [line.id, formatQuantityInput(line.final_scaled ?? line.counted_scaled, line.quantity_scale)]))); };
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!selected) return;
    const lines = selected.lines.map((line) => ({ lineId: line.id, finalScaled: quantityInputToScaled(finals[line.id] ?? '', line.quantity_scale) }));
    if (lines.some((line) => line.finalScaled === null)) { setMessage('أدخل الكمية النهائية لكل صنف.'); return; }
    setMessage('');
    try { await onDecide(selected.id, { expectedVersion: selected.version, reason, lines: lines.map((line) => ({ lineId: line.lineId, finalScaled: line.finalScaled as string })) }); await refresh(); }
    catch (caught) { setMessage(friendlyError(caught)); }
  };
  if (selected) return <div className="page-stack"><button className="back-button" onClick={() => setSelectedId(null)}>→ كل التعارضات</button><section className="branch-title"><div className="cupcake-site-icon large"><ClipboardCheck /></div><div><span>{selected.site.name}</span><h2>{selected.reference}</h2><p>تم الإبلاغ {formatDate(selected.reported_at)}</p></div><span className={`pill ${selected.status === 'OPEN' ? 'danger' : selected.status === 'PENDING_SITE_APPLY' ? 'warning' : 'success'}`}>{conflictStatusLabel(selected.status)}</span></section>{message && <div className="form-error" role="alert">{message}</div>}<section className="panel table-panel"><PanelHeading title="مقارنة الكميات" subtitle="الكمية النهائية تُرسل كقرار موثق ولا تعدل السجل الأصلي" /><div className="table-wrap"><table><thead><tr><th>الصنف</th><th>المرسل</th><th>المعدود</th><th>النهائي</th></tr></thead><tbody>{selected.lines.map((line) => <tr key={line.id}><td><strong>{line.name_ar}</strong><small className="cell-note">{line.note}</small></td><td>{formatQuantity(line.sent_scaled, line.quantity_scale)} {line.unit}</td><td>{formatQuantity(line.counted_scaled, line.quantity_scale)} {line.unit}</td><td>{selected.status === 'OPEN' ? <input className="quantity-cell" inputMode="decimal" value={finals[line.id] ?? ''} onChange={(event) => setFinals({ ...finals, [line.id]: event.target.value })} /> : `${formatQuantity(line.final_scaled ?? line.counted_scaled, line.quantity_scale)} ${line.unit}`}</td></tr>)}</tbody></table></div></section>{selected.status === 'OPEN' ? <form className="panel decision-form" onSubmit={submit}><label>سبب القرار<input value={reason} onChange={(event) => setReason(event.target.value)} minLength={3} placeholder="مثال: اعتماد الكمية المعدودة بعد مراجعة الفرع" required /></label><button className="primary-button">حفظ وإرسال القرار</button></form> : selected.decision && <section className="customer-note"><strong>قرار {selected.decision.admin_name}</strong><span>{selected.decision.reason} · {conflictApplicationLabel(selected.decision.application_status)}</span></section>}</div>;
  return <div className="page-stack">{message && <div className="form-error" role="alert">{message}</div>}{!conflicts ? <div className="panel mini-loading"><RefreshCw className="spin" /> جارٍ تحميل التعارضات…</div> : <section className="conflict-tiles">{conflicts.map((conflict) => <button key={conflict.id} onClick={() => choose(conflict)}><span className="cupcake-site-icon"><ClipboardCheck /></span><span><strong>{conflict.reference}</strong><small>{conflict.site.name} · {conflict.lines.length} أصناف</small></span><span className={`pill ${conflict.status === 'OPEN' ? 'danger' : conflict.status === 'PENDING_SITE_APPLY' ? 'warning' : 'success'}`}>{conflictStatusLabel(conflict.status)}</span><ChevronLeft /></button>)}{!conflicts.length && <EmptyState icon={ClipboardCheck} title="لا توجد تعارضات" text="كل الكميات المستلمة متطابقة حالياً." />}</section>}</div>;
}

function TeamPage({ users, roles, sites, onCreateUser, onUpdateUser, onArchiveUser, onCreateRole, onUpdateRole, onArchiveRole }: {
  users: User[];
  roles: Role[];
  sites: Site[];
  onCreateUser: (input: { username: string; displayName: string; password: string; roleId: string; siteId?: string }) => Promise<void>;
  onUpdateUser: (id: string, input: { username?: string; displayName?: string; password?: string; active?: boolean; roleId?: string; siteId?: string }) => Promise<void>;
  onArchiveUser: (id: string) => Promise<void>;
  onCreateRole: (input: { code: string; name: string; permissions: string[] }) => Promise<void>;
  onUpdateRole: (id: string, input: { code?: string; name?: string; permissions?: string[]; active?: boolean }) => Promise<void>;
  onArchiveRole: (id: string) => Promise<void>;
}) {
  const [tab, setTab] = useState<'users' | 'roles'>('users');
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [message, setMessage] = useState('');
  const [changePassword, setChangePassword] = useState(false);
  const [userForm, setUserForm] = useState({ username: '', displayName: '', password: '', roleId: roles[0]?.id || '', siteId: '' });
  const [roleForm, setRoleForm] = useState<{ code: string; name: string; permissions: string[] }>({ code: '', name: '', permissions: [] });

  useEffect(() => {
    if (!userForm.roleId) {
      const firstActiveRole = roles.find((role) => role.active !== false);
      if (firstActiveRole) setUserForm((current) => ({ ...current, roleId: firstActiveRole.id }));
    }
  }, [roles, userForm.roleId]);

  const reset = (nextTab = tab) => {
    setEditingId(null); setChangePassword(false); setMessage('');
    setUserForm({ username: '', displayName: '', password: '', roleId: roles.find((role) => role.active !== false)?.id || '', siteId: '' });
    setRoleForm({ code: '', name: '', permissions: [] });
    setShowForm(false); setTab(nextTab);
  };
  const editUser = (user: User) => { setChangePassword(false); const assignment = user.siteRoles[0]; setTab('users'); setEditingId(user.id); setUserForm({ username: user.username, displayName: user.displayName, password: '', roleId: assignment?.role.id || roles.find((role) => role.active !== false)?.id || '', siteId: assignment?.site?.id || '' }); setShowForm(true); };
  const editRole = (role: Role) => { setTab('roles'); setEditingId(role.id); setRoleForm({ code: role.code, name: role.name, permissions: role.permissions }); setShowForm(true); };
  const submitUser = async (event: FormEvent) => { event.preventDefault(); setMessage(''); if (!roles.some((role) => role.id === userForm.roleId && role.active !== false)) { setMessage('اختر دوراً صالحاً قبل الحفظ.'); return; } try { const input = { ...userForm, ...(userForm.siteId ? {} : { siteId: undefined }), ...(!editingId || changePassword ? {} : { password: undefined }) }; if (editingId) await onUpdateUser(editingId, input); else await onCreateUser({ ...userForm, ...(userForm.siteId ? {} : { siteId: undefined }) }); reset('users'); } catch (caught) { setMessage(friendlyError(caught)); } };
  const submitRole = async (event: FormEvent) => { event.preventDefault(); setMessage(''); try { if (editingId) await onUpdateRole(editingId, roleForm); else await onCreateRole(roleForm); reset('roles'); } catch (caught) { setMessage(friendlyError(caught)); } };
  const archive = async (id: string) => { if (confirmId !== id) { setConfirmId(id); return; } setMessage(''); try { if (tab === 'users') await onArchiveUser(id); else await onArchiveRole(id); setConfirmId(null); } catch (caught) { setMessage(friendlyError(caught)); } };

  return <div className="page-stack">
    <div className="tab-toolbar"><div><button className={tab === 'users' ? 'active' : ''} onClick={() => reset('users')}>المستخدمون <span>{users.length}</span></button><button className={tab === 'roles' ? 'active' : ''} onClick={() => reset('roles')}>الأدوار <span>{roles.length}</span></button></div><button className="primary-button" onClick={() => { setEditingId(null); setShowForm((value) => !value); }}>{tab === 'users' ? '+ مستخدم جديد' : '+ دور جديد'}</button></div>
    {message && <div className="form-error" role="alert">{message}</div>}
    {showForm && tab === 'users' && <form className="panel inline-form user-form" onSubmit={submitUser}><label>الاسم<input value={userForm.displayName} onChange={(event) => setUserForm({ ...userForm, displayName: event.target.value })} required /></label><label>اسم المستخدم<input className="ltr" value={userForm.username} onChange={(event) => setUserForm({ ...userForm, username: event.target.value })} required /></label>{editingId && <label><span><input type="checkbox" checked={changePassword} onChange={(event) => { setChangePassword(event.target.checked); setUserForm({ ...userForm, password: '' }); }} /> تغيير كلمة المرور</span><small>منح الصلاحيات يحافظ على كلمة المرور الحالية</small></label>}{(!editingId || changePassword) && <label>كلمة المرور الجديدة<input type="password" autoComplete="new-password" value={userForm.password} onChange={(event) => setUserForm({ ...userForm, password: event.target.value })} required minLength={12} placeholder="١٢ حرفاً على الأقل" /></label>}<label>الدور<select value={userForm.roleId} onChange={(event) => setUserForm({ ...userForm, roleId: event.target.value })} required>{roles.filter((role) => role.active !== false).map((role) => <option value={role.id} key={role.id}>{role.name}</option>)}</select></label><label>نطاق الموقع<select value={userForm.siteId} onChange={(event) => setUserForm({ ...userForm, siteId: event.target.value })}><option value="">كل المواقع</option>{sites.filter((site) => site.active).map((site) => <option value={site.id} key={site.id}>{site.name}</option>)}</select></label><button className="primary-button">{editingId ? 'حفظ' : 'إضافة'}</button></form>}
    {showForm && tab === 'roles' && <form className="panel role-editor" onSubmit={submitRole}><div className="inline-form role-name-fields"><label>الكود<input className="ltr" value={roleForm.code} onChange={(event) => setRoleForm({ ...roleForm, code: event.target.value })} required /></label><label>اسم الدور<input value={roleForm.name} onChange={(event) => setRoleForm({ ...roleForm, name: event.target.value })} required /></label></div><fieldset><legend>اختَر ما يستطيع هذا الدور فعله</legend><div className="permission-grid">{permissionOptions.map((permission) => <label key={permission.code} className={roleForm.permissions.includes(permission.code) ? 'selected' : ''}><input type="checkbox" checked={roleForm.permissions.includes(permission.code)} onChange={(event) => { const next = event.target.checked ? [...roleForm.permissions.filter((code) => permission.code === '*' || code !== '*'), permission.code] : roleForm.permissions.filter((code) => code !== permission.code); setRoleForm({ ...roleForm, permissions: permission.code === '*' && event.target.checked ? ['*'] : next }); }} /><span><strong>{permission.label}</strong><small className="ltr">{permission.code}</small></span></label>)}</div></fieldset><div className="form-actions"><button className="primary-button">{editingId ? 'حفظ الصلاحيات' : 'إضافة الدور'}</button><button type="button" className="secondary-button" onClick={() => reset('roles')}>إلغاء</button></div></form>}
    {tab === 'users' ? <section className="panel table-panel"><PanelHeading title="المستخدمون" subtitle="حسابات مسمّاة؛ لا تُعرض كلمات المرور أو تجزئاتها" /><div className="table-wrap"><table><thead><tr><th>الاسم</th><th>اسم المستخدم</th><th>الدور</th><th>النطاق</th><th>الحالة</th><th>إجراءات</th></tr></thead><tbody>{users.map((user) => <tr key={user.id}><td><strong>{user.displayName}</strong></td><td className="ltr">{user.username}</td><td>{user.siteRoles.map((entry) => entry.role.name).join('، ') || 'بدون دور'}</td><td>{user.siteRoles.map((entry) => entry.site?.name || 'كل المواقع').join('، ') || '—'}</td><td><span className={`pill ${user.status === 'PENDING_PERMISSION' ? 'warning' : user.active ? 'success' : 'neutral'}`}>{user.status === 'PENDING_PERMISSION' ? 'بانتظار الصلاحيات' : user.active ? 'نشط' : 'موقوف'}</span></td><td><div className="row-actions"><button onClick={() => editUser(user)}>{user.status === 'PENDING_PERMISSION' ? 'منح صلاحية' : 'تعديل'}</button><button disabled={!user.active} className={confirmId === user.id ? 'confirm-delete' : ''} onClick={() => void archive(user.id)}>{confirmId === user.id ? 'تأكيد' : 'إيقاف'}</button></div></td></tr>)}</tbody></table></div></section> : <section className="panel role-list"><PanelHeading title="الأدوار والصلاحيات" subtitle="صلاحيات واضحة قابلة للتعديل وتُخزن على الخادم" />{roles.map((role) => <div key={role.id}><span className="role-icon"><ShieldCheck /></span><div className="grow"><strong>{role.name}</strong><small>{role.code}</small><div className="permission-chips">{role.permissions.includes('*') ? <span>كل صلاحيات النظام</span> : role.permissions.slice(0, 4).map((permission) => <span key={permission}>{permissionLabel(permission)}</span>)}{role.permissions.length > 4 && <span>+{role.permissions.length - 4}</span>}</div></div><span className={`pill ${role.active !== false ? 'success' : 'neutral'}`}>{role.active !== false ? 'نشط' : 'مؤرشف'}</span><div className="row-actions"><button onClick={() => editRole(role)}>تعديل</button><button disabled={role.active === false} className={confirmId === role.id ? 'confirm-delete' : ''} onClick={() => void archive(role.id)}>{confirmId === role.id ? 'تأكيد' : 'أرشفة'}</button></div></div>)}</section>}
  </div>;
}

function OperationsPage({ data, onIssueEnrollmentToken, token }: { data: AdminData; token: string; onIssueEnrollmentToken: (siteId: string) => Promise<{ id: string; token: string; siteId: string; profile: SiteType; expiresAt: string }> }) {
  const [enrollment, setEnrollment] = useState<{ token: string; siteId: string; expiresAt: string } | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [downloadProgress, setDownloadProgress] = useState(0);
  const [busySite, setBusySite] = useState<string | null>(null);
  const [message, setMessage] = useState('');
  const [branchOneRelease, setBranchOneRelease] = useState<Awaited<ReturnType<typeof api.branchOneRelease>> | null>(null);
  const [branchOneTouchRelease, setBranchOneTouchRelease] = useState<Awaited<ReturnType<typeof api.branchOneTouchRelease>> | null>(null);
  const [kitchenRelease, setKitchenRelease] = useState<Awaited<ReturnType<typeof api.kitchenRelease>> | null>(null);
  const [branchTwoRelease, setBranchTwoRelease] = useState<Awaited<ReturnType<typeof api.branchTwoRelease>> | null>(null);
  useEffect(() => { void api.branchOneRelease(token).then(setBranchOneRelease).catch(() => setBranchOneRelease(null)); }, [token]);
  useEffect(() => { void api.branchOneTouchRelease(token).then(setBranchOneTouchRelease).catch(() => setBranchOneTouchRelease(null)); }, [token]);
  useEffect(() => { void api.kitchenRelease(token).then(setKitchenRelease).catch(() => setKitchenRelease(null)); }, [token]);
  useEffect(() => { void api.branchTwoRelease(token).then(setBranchTwoRelease).catch(() => setBranchTwoRelease(null)); }, [token]);
  return <div className="page-stack">
    <section className="panel enrollment-panel"><PanelHeading title="إعداد أجهزة الفروع" subtitle="عنوان الخادم والمواقع محفوظان هنا؛ أنشئ رمز ربط جديداً عند إعداد كل جهاز" /><div className="detail-list"><div><dt>عنوان الخادم في التطبيق</dt><dd className="ltr">https://ascendyz.xyz/api/v1</dd></div><div><dt>طريقة الربط</dt><dd>الإعدادات ← ربط الجهاز بالخادم ← الصق العنوان والرمز</dd></div></div><div className="enrollment-sites">{data.sites.filter((site) => site.active).map((site) => <button key={site.id} disabled={busySite === site.id} onClick={async () => { setBusySite(site.id); setMessage(''); try { const result = await onIssueEnrollmentToken(site.id); setEnrollment(result); } catch (caught) { setMessage(friendlyError(caught)); } finally { setBusySite(null); } }}><span className="site-icon"><MonitorSmartphone /></span><span><strong>{site.name}</strong><small>{site.code} · {siteTypeLabels[site.type]}</small></span><span>إنشاء رمز ربط</span></button>)}</div>{message && <div className="form-error">{message}</div>}{enrollment && <div className="one-time-token"><div><strong>رمز ربط لمرة واحدة</strong><small>ينتهي {formatDate(enrollment.expiresAt)}. انسخه الآن؛ يمكنك إصدار رمز جديد لاحقاً.</small></div><code>{enrollment.token}</code><button className="secondary-button" onClick={() => { void navigator.clipboard.writeText(enrollment.token); }}>نسخ الرمز</button></div>}</section>
    <section className="panel table-panel"><PanelHeading title="الأجهزة والمزامنة" subtitle="حالة التسجيل وآخر اتصال لكل جهاز" /><div className="table-wrap"><table><thead><tr><th>الجهاز</th><th>الموقع</th><th>الملف</th><th>التسجيل</th><th>آخر ظهور</th><th>الإصدار</th></tr></thead><tbody>{data.devices.map((device) => <tr key={device.id}><td className="ltr">{device.id.slice(0, 8)}</td><td>{data.sites.find((site) => site.id === device.siteId)?.name || 'موقع غير معروف'}</td><td>{siteTypeLabels[device.profile]}</td><td><span className={`pill ${device.enrollmentStatus === 'ENROLLED' ? 'success' : device.enrollmentStatus === 'REVOKED' ? 'danger' : 'warning'}`}>{device.enrollmentStatus === 'ENROLLED' ? 'مسجل' : device.enrollmentStatus === 'REVOKED' ? 'ملغي' : 'بانتظار التسجيل'}</span></td><td>{device.lastSeenAt ? formatDate(device.lastSeenAt) : 'لم يتصل بعد'}</td><td>{device.appVersion || '—'}</td></tr>)}</tbody></table></div>{!data.devices.length && <EmptyState icon={MonitorSmartphone} title="لا توجد أجهزة" text="سيظهر الجهاز بعد إصدار رمز تسجيل لأحد المواقع." />}</section>
    <section className="two-column">
      <article className="panel backup-panel"><PanelHeading title="النسخ الاحتياطي" subtitle="PostgreSQL وسجل الملفات التشغيلية" /><div className="backup-visual"><DatabaseBackup size={34} /><div><strong>لم تُسجل نسخة خارجية بعد</strong><p>يلزم تحديد وجهة VPS مشفّرة وسياسة الاحتفاظ قبل التفعيل.</p></div></div><dl className="detail-list"><div><dt>قاعدة البيانات</dt><dd><span className="status-good">جاهزة</span></dd></div><div><dt>الهدف المقترح</dt><dd>RPO ساعة / RTO ٤ ساعات</dd></div><div><dt>اختبار الاستعادة</dt><dd>بانتظار إعداد الوجهة</dd></div></dl><button className="secondary-button" disabled>تشغيل نسخة الآن</button></article>
      <article className="panel"><PanelHeading title="تقارير الورديات" subtitle="ملفات Excel الأصلية لا تُستبدل" /><EmptyState compact icon={FileSpreadsheet} title="لا توجد تقارير مرفوعة" text="ستظهر ملفات .xlsx بعد إغلاق أول وردية ومزامنتها." /></article>
    </section>
    <section className="panel installers">{message && <div className="form-error" role="alert">{message}</div>}<PanelHeading title="تنزيل تطبيقات نقاط التشغيل" subtitle="مثبتات Windows ذاتية الاحتواء؛ تحقق من SHA-256 بعد التنزيل" /><div className="installer-grid">{([{ label: 'فرع نوع ١ · مكتبي', release: branchOneRelease, variant: 'desktop' }, { label: 'فرع نوع ١ · لمس', release: branchOneTouchRelease, variant: 'touch' }, { label: 'المطبخ · كمبيوتر مكتبي', release: kitchenRelease, variant: 'kitchen' }, { label: siteTypeLabels.BRANCH_TYPE_2, release: branchTwoRelease, variant: 'branch2' }] as const).map(({ label, release, variant }) => <article key={label}><div className="installer-icon"><Download /></div><div><h3>{label}</h3><p>{release ? `Windows x64 · ${release.version} · ${formatDate(release.publishedAt)}` : 'لا يوجد إصدار منشور حالياً'}</p></div><button className="secondary-button" disabled={!release || !variant || downloading !== null} onClick={async () => { if (!variant) return; setDownloading(variant); setDownloadProgress(0); setMessage(''); try { if (variant === 'kitchen') await api.downloadKitchen(token, setDownloadProgress); else if (variant === 'branch2') await api.downloadBranchTwo(token, setDownloadProgress); else await api.downloadBranchOne(token, variant, setDownloadProgress); } catch (caught) { setMessage(friendlyError(caught)); } finally { setDownloading(null); } }}><Download size={17} /> {downloading === variant ? `جارٍ التنزيل ${downloadProgress}%` : release ? 'تنزيل المثبت' : 'غير متاح'}</button>{release && <small className="ltr">SHA-256: {release.sha256}</small>}</article>)}</div></section>
  </div>;
}

function StatCard({ label, value, note, icon, tone }: { label: string; value: string; note: string; icon: LucideIcon; tone: string }) {
  const Icon = icon;
  return <article className="stat-card"><div className={`stat-icon ${tone}`}><Icon /></div><div><span>{label}</span><strong>{value}</strong><small>{note}</small></div></article>;
}

function PanelHeading({ title, subtitle, action, onAction }: { title: string; subtitle: string; action?: string; onAction?: () => void }) {
  return <div className="panel-heading"><div><h2>{title}</h2><p>{subtitle}</p></div>{action && <button onClick={onAction}>{action} <ChevronLeft size={16} /></button>}</div>;
}

function SectionToolbar({ count, action, onAction }: { count: string; action: string; onAction: () => void }) {
  return <div className="section-toolbar"><span className="pill neutral">{count}</span><button className="primary-button" onClick={onAction}>+ {action}</button></div>;
}

function EmptyState({ icon, title, text, compact = false }: { icon: LucideIcon; title: string; text: string; compact?: boolean }) {
  const Icon = icon;
  return <div className={`empty-state ${compact ? 'compact' : ''}`}><Icon /><strong>{title}</strong><p>{text}</p></div>;
}

function LoadingPage() {
  return <div className="loading-page" aria-label="جارٍ تحميل البيانات"><div className="loading-mark">S</div><p>جارٍ تحميل بيانات الإدارة…</p></div>;
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('ar-EG', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Africa/Cairo' }).format(new Date(value));
}

function formatMoney(minorUnits: string) {
  return new Intl.NumberFormat('ar-EG', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(Number(minorUnits) / 100);
}

function moneyMinorToInput(minorUnits: number) {
  return (minorUnits / 100).toFixed(2);
}

function moneyInputToMinor(value: string): number | null {
  const normalized = value.trim().replace(',', '.');
  if (!/^\d+(?:\.\d{1,2})?$/.test(normalized)) return null;
  const [whole, fraction = ''] = normalized.split('.');
  const minor = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
  return Number.isSafeInteger(minor) && minor <= 2_000_000_000 ? minor : null;
}

function formatQuantity(scaledValue: string, scale: number) {
  return new Intl.NumberFormat('ar-EG', { maximumFractionDigits: Math.max(0, Math.ceil(Math.log10(scale))) }).format(Number(scaledValue) / scale);
}

function formatQuantityInput(scaledValue: string, scale: number) {
  return String(Number(scaledValue) / scale);
}

function quantityInputToScaled(value: string, scale: number): string | null {
  const normalized = value.trim().replace(',', '.');
  if (!/^\d+(?:\.\d+)?$/.test(normalized)) return null;
  const scaled = Math.round(Number(normalized) * scale);
  return Number.isSafeInteger(scaled) && scaled >= 0 ? String(scaled) : null;
}

function conflictStatusLabel(status: QuantityConflict['status']) {
  return status === 'OPEN' ? 'يحتاج قراراً' : status === 'PENDING_SITE_APPLY' ? 'بانتظار تطبيق الموقع' : 'تم الحل';
}

function conflictApplicationLabel(status: NonNullable<QuantityConflict['decision']>['application_status']) {
  const labels = { CREATED: 'تم إنشاء الأمر', DELIVERED: 'وصل للموقع', APPLIED: 'طُبق محلياً', REJECTED: 'تعذر التطبيق' } as const;
  return labels[status];
}

function locationLabel(location: BranchOverview['stock'][number]['location']) {
  const labels: Record<BranchOverview['stock'][number]['location'], string> = {
    SALEABLE: 'متاح للبيع',
    FREEZER: 'الفريزر',
    DISPLAY: 'العرض',
    KITCHEN: 'المطبخ',
    HOLD: 'محجوز',
    TRANSIT: 'في الطريق',
  };
  return labels[location];
}

export default App;
