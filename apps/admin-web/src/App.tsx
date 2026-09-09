import {
  Activity,
  AlertTriangle,
  ArchiveRestore,
  Boxes,
  Building2,
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
import { ApiError, api, type Device, type Item, type Role, type Site, type SiteType, type User } from './api';

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

function App() {
  const [token, setToken] = useState(() => sessionStorage.getItem('sugar_admin_token'));

  const signIn = (nextToken: string) => {
    sessionStorage.setItem('sugar_admin_token', nextToken);
    setToken(nextToken);
  };

  const signOut = () => {
    sessionStorage.removeItem('sugar_admin_token');
    setToken(null);
  };

  return token ? <AdminApp token={token} onSignOut={signOut} /> : <LoginScreen onSignIn={signIn} />;
}

function LoginScreen({ onSignIn }: { onSignIn: (token: string) => void }) {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      const result = await api.login(username, password);
      onSignIn(result.access_token);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'تعذر الاتصال بالخادم');
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
        </div>
        <div className="login-status"><ShieldCheck size={20} /> اتصال محلي مشفّر داخل بيئة التطوير</div>
      </section>
      <section className="login-panel">
        <form className="login-card" onSubmit={submit}>
          <div className="mobile-brand"><div className="brand-mark">S</div><strong>Sugar ERP</strong></div>
          <p className="eyebrow dark">لوحة الإدارة</p>
          <h2>تسجيل الدخول</h2>
          <p className="muted">استخدم حسابك الإداري للوصول إلى بيانات الشركة.</p>
          <label>
            اسم المستخدم
            <input value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="username" required />
          </label>
          <label>
            كلمة المرور
            <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" required />
          </label>
          {error && <div className="form-error" role="alert"><AlertTriangle size={18} /> {error}</div>}
          <button className="primary-button login-button" disabled={busy}>
            {busy ? <><RefreshCw className="spin" size={19} /> جارٍ الدخول</> : <>دخول آمن <ChevronLeft size={19} /></>}
          </button>
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
      setError(caught instanceof Error ? caught.message : 'تعذر تحميل البيانات');
    } finally {
      setLoading(false);
    }
  }, [onSignOut, token]);

  useEffect(() => { void loadData(); }, [loadData]);
  useEffect(() => {
    document.body.classList.toggle('touch-mode', touchMode);
    localStorage.setItem('sugar_touch_mode', String(touchMode));
  }, [touchMode]);

  const navigate = (key: PageKey) => {
    setPage(key);
    setMenuOpen(false);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const createSite = async (input: Pick<Site, 'code' | 'name' | 'type'>) => {
    const site = await api.createSite(token, input);
    setData((current) => ({ ...current, sites: [...current.sites, site].sort((a, b) => a.code.localeCompare(b.code)) }));
  };

  const createItem = async (input: Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'kind'>) => {
    const item = await api.createItem(token, input);
    setData((current) => ({ ...current, items: [...current.items, item].sort((a, b) => a.sku.localeCompare(b.sku)) }));
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
          {loading && !lastLoaded ? <LoadingPage /> : (
            <PageContent
              page={page}
              data={data}
              lastLoaded={lastLoaded}
              onCreateSite={createSite}
              onCreateItem={createItem}
              onNavigate={navigate}
            />
          )}
        </main>
      </div>
    </div>
  );
}

function PageContent({ page, data, lastLoaded, onCreateSite, onCreateItem, onNavigate }: {
  page: PageKey;
  data: AdminData;
  lastLoaded: Date | null;
  onCreateSite: (input: Pick<Site, 'code' | 'name' | 'type'>) => Promise<void>;
  onCreateItem: (input: Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'kind'>) => Promise<void>;
  onNavigate: (page: PageKey) => void;
}) {
  switch (page) {
    case 'dashboard': return <Dashboard data={data} lastLoaded={lastLoaded} onNavigate={onNavigate} />;
    case 'branches': return <BranchesPage data={data} onCreate={onCreateSite} />;
    case 'cafe': return <CafePage />;
    case 'kitchen': return <KitchenPage data={data} />;
    case 'conflicts': return <ConflictsPage />;
    case 'catalog': return <CatalogPage items={data.items} onCreate={onCreateItem} />;
    case 'team': return <TeamPage users={data.users} roles={data.roles} />;
    case 'operations': return <OperationsPage data={data} />;
  }
}

function Dashboard({ data, lastLoaded, onNavigate }: { data: AdminData; lastLoaded: Date | null; onNavigate: (page: PageKey) => void }) {
  const branches = data.sites.filter((site) => site.type !== 'KITCHEN');
  const kitchens = data.sites.filter((site) => site.type === 'KITCHEN');
  const enrolled = data.devices.filter((device) => device.enrollmentStatus === 'ENROLLED').length;
  const freshness = lastLoaded ? new Intl.DateTimeFormat('ar-EG', { hour: '2-digit', minute: '2-digit', timeZone: 'Africa/Cairo' }).format(lastLoaded) : '—';

  return (
    <div className="page-stack">
      <section className="status-ribbon">
        <div><span className="live-dot" /> <strong>الخادم متصل</strong><span>آخر تحديث {freshness}</span></div>
        <button onClick={() => onNavigate('operations')}>تفاصيل التشغيل <ChevronLeft size={17} /></button>
      </section>
      <section className="stats-grid">
        <StatCard label="الفروع النشطة" value={String(branches.length)} note="مواقع بيع مسجلة" icon={Store} tone="indigo" />
        <StatCard label="المطبخ المركزي" value={String(kitchens.length)} note="موقع إنتاج مسجل" icon={ChefHat} tone="orange" />
        <StatCard label="الأجهزة المسجلة" value={`${enrolled}/${data.devices.length}`} note="مسجل / إجمالي" icon={MonitorSmartphone} tone="blue" />
        <StatCard label="الأصناف" value={String(data.items.length)} note="منتجات وخامات" icon={Boxes} tone="green" />
      </section>
      <section className="dashboard-grid">
        <article className="panel wide-panel">
          <PanelHeading title="حالة المواقع" subtitle="بيانات حقيقية من قاعدة PostgreSQL" action="عرض الفروع" onAction={() => onNavigate('branches')} />
          {data.sites.length ? <div className="site-overview-list">{data.sites.map((site) => {
            const siteDevices = data.devices.filter((device) => device.siteId === site.id);
            return <div className="site-overview-row" key={site.id}>
              <div className={`site-icon ${site.type === 'KITCHEN' ? 'kitchen' : ''}`}>{site.type === 'KITCHEN' ? <ChefHat /> : <Building2 />}</div>
              <div className="grow"><strong>{site.name}</strong><span>{site.code} · {siteTypeLabels[site.type]}</span></div>
              <div className="sync-cell"><span className={siteDevices.some((device) => device.enrollmentStatus === 'ENROLLED') ? 'status-good' : 'status-warn'}>{siteDevices.some((device) => device.enrollmentStatus === 'ENROLLED') ? 'متصل' : 'بانتظار التسجيل'}</span><small>{siteDevices.length} جهاز</small></div>
            </div>;
          })}</div> : <EmptyState icon={Store} title="لا توجد مواقع بعد" text="أضف أول فرع أو مطبخ لبدء الإعداد." />}
        </article>
        <article className="panel attention-panel">
          <PanelHeading title="تحتاج انتباهك" subtitle="متابعة التشغيل المركزي" />
          <div className="attention-list">
            <button onClick={() => onNavigate('operations')}><span className="attention-icon amber"><MonitorSmartphone /></span><span><strong>{data.devices.filter((device) => device.enrollmentStatus === 'PENDING').length} أجهزة</strong><small>بانتظار إتمام التسجيل</small></span><ChevronLeft /></button>
            <button onClick={() => onNavigate('conflicts')}><span className="attention-icon violet"><ClipboardCheck /></span><span><strong>لا تعارضات واردة</strong><small>ستظهر بعد أول مزامنة تشغيلية</small></span><ChevronLeft /></button>
            <button onClick={() => onNavigate('operations')}><span className="attention-icon blue"><DatabaseBackup /></span><span><strong>النسخ الاحتياطي</strong><small>جاهز للإعداد في بيئة VPS</small></span><ChevronLeft /></button>
          </div>
        </article>
      </section>
      <section className="panel data-waiting">
        <div><Activity size={23} /><div><strong>بيانات المبيعات والتحصيل</strong><p>لم تصل معاملات تشغيلية من أجهزة الفروع بعد. لن تعرض اللوحة أرقاماً تقديرية أو أرباحاً غير محسوبة.</p></div></div>
        <span className="pill neutral">بانتظار أول مزامنة</span>
      </section>
    </div>
  );
}

function BranchesPage({ data, onCreate }: { data: AdminData; onCreate: (input: Pick<Site, 'code' | 'name' | 'type'>) => Promise<void> }) {
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [type, setType] = useState<SiteType>('BRANCH_TYPE_1');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setMessage('');
    try { await onCreate({ code, name, type }); setCode(''); setName(''); setShowForm(false); }
    catch (caught) { setMessage(caught instanceof Error ? caught.message : 'تعذر إضافة الموقع'); }
    finally { setBusy(false); }
  };

  return <div className="page-stack">
    <SectionToolbar count={`${data.sites.length} مواقع`} action="إضافة موقع" onAction={() => setShowForm((value) => !value)} />
    {showForm && <form className="panel inline-form" onSubmit={submit}>
      <div><label>كود الموقع<input value={code} onChange={(event) => setCode(event.target.value)} placeholder="BRANCH-03" required /></label></div>
      <div><label>اسم الموقع<input value={name} onChange={(event) => setName(event.target.value)} placeholder="فرع المعادي" required /></label></div>
      <div><label>نوع الموقع<select value={type} onChange={(event) => setType(event.target.value as SiteType)}><option value="BRANCH_TYPE_1">فرع نوع ١</option><option value="BRANCH_TYPE_2">فرع نوع ٢</option><option value="KITCHEN">مطبخ</option></select></label></div>
      <button className="primary-button" disabled={busy}>{busy ? 'جارٍ الحفظ' : 'حفظ الموقع'}</button>
      {message && <p className="form-error">{message}</p>}
    </form>}
    <section className="card-grid">
      {data.sites.map((site) => {
        const devices = data.devices.filter((device) => device.siteId === site.id);
        return <article className="site-card" key={site.id}>
          <div className="site-card-head"><div className={`site-icon ${site.type === 'KITCHEN' ? 'kitchen' : ''}`}>{site.type === 'KITCHEN' ? <ChefHat /> : <Store />}</div><span className={`pill ${site.active ? 'success' : 'neutral'}`}>{site.active ? 'نشط' : 'متوقف'}</span></div>
          <h2>{site.name}</h2><p>{site.code} · {siteTypeLabels[site.type]}</p>
          <dl><div><dt>الأجهزة</dt><dd>{devices.length}</dd></div><div><dt>المنطقة الزمنية</dt><dd>{site.timezone}</dd></div></dl>
          <div className="card-footer"><span className={devices.some((device) => device.enrollmentStatus === 'ENROLLED') ? 'status-good' : 'status-warn'}>{devices.some((device) => device.enrollmentStatus === 'ENROLLED') ? 'المزامنة متاحة' : 'بانتظار جهاز'}</span></div>
        </article>;
      })}
      {!data.sites.length && <EmptyState icon={Store} title="لا توجد مواقع" text="استخدم زر إضافة موقع للبدء." />}
    </section>
  </div>;
}

function CatalogPage({ items, onCreate }: { items: Item[]; onCreate: (input: Pick<Item, 'sku' | 'nameAr' | 'unit' | 'quantityScale' | 'kind'>) => Promise<void> }) {
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ sku: '', nameAr: '', unit: 'قطعة', quantityScale: 1, kind: 'PRODUCT' as Item['kind'] });
  const [message, setMessage] = useState('');
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setMessage('');
    try { await onCreate(form); setForm({ sku: '', nameAr: '', unit: 'قطعة', quantityScale: 1, kind: 'PRODUCT' }); setShowForm(false); }
    catch (caught) { setMessage(caught instanceof Error ? caught.message : 'تعذر إضافة الصنف'); }
  };
  return <div className="page-stack">
    <SectionToolbar count={`${items.length} أصناف`} action="إضافة صنف" onAction={() => setShowForm((value) => !value)} />
    {showForm && <form className="panel inline-form catalog-form" onSubmit={submit}>
      <label>الكود<input value={form.sku} onChange={(event) => setForm({ ...form, sku: event.target.value })} required /></label>
      <label>الاسم العربي<input value={form.nameAr} onChange={(event) => setForm({ ...form, nameAr: event.target.value })} required /></label>
      <label>الوحدة<input value={form.unit} onChange={(event) => setForm({ ...form, unit: event.target.value })} required /></label>
      <label>النوع<select value={form.kind} onChange={(event) => setForm({ ...form, kind: event.target.value as Item['kind'] })}><option value="PRODUCT">منتج</option><option value="INGREDIENT">خامة</option></select></label>
      <button className="primary-button">حفظ الصنف</button>{message && <p className="form-error">{message}</p>}
    </form>}
    <section className="panel table-panel"><div className="table-wrap"><table><thead><tr><th>الصنف</th><th>الكود</th><th>النوع</th><th>الوحدة</th><th>الإصدار</th><th>الحالة</th></tr></thead><tbody>{items.map((item) => <tr key={item.id}><td><strong>{item.nameAr}</strong></td><td className="ltr">{item.sku}</td><td>{item.kind === 'PRODUCT' ? 'منتج' : 'خامة'}</td><td>{item.unit}</td><td>{item.version}</td><td><span className={`pill ${item.active ? 'success' : 'neutral'}`}>{item.active ? 'نشط' : 'متوقف'}</span></td></tr>)}</tbody></table></div>{!items.length && <EmptyState icon={PackageSearch} title="الكتالوج فارغ" text="أضف المنتجات والخامات المركزية." />}</section>
  </div>;
}

function CafePage() {
  return <EmptyBusinessPage icon={CircleDollarSign} title="لا توجد حسابات كافيه متزامنة" text="عند وصول أول فاتورة ستظهر هنا قيمة الفواتير، المحصّل، والمتبقي لكل عميل بشكل منفصل." chips={['الفواتير الصادرة', 'التحصيلات', 'الرصيد المتبقي', 'تخصيص الدفعات']} />;
}

function KitchenPage({ data }: { data: AdminData }) {
  const ingredients = data.items.filter((item) => item.kind === 'INGREDIENT');
  return <div className="page-stack"><section className="stats-grid compact"><StatCard label="الخامات المسجلة" value={String(ingredients.length)} note="في الكتالوج المركزي" icon={Boxes} tone="orange" /><StatCard label="تنبيهات النقص" value="—" note="بانتظار حركة المخزون" icon={AlertTriangle} tone="indigo" /><StatCard label="الهالك المسجل" value="—" note="لا توجد ورديات متزامنة" icon={ArchiveRestore} tone="blue" /></section><EmptyBusinessPage icon={ChefHat} title="بانتظار بيانات تشغيل المطبخ" text="الاستهلاك حسب وصفة الإرسال، الهالك، والجرد الفعلي سيظهرون هنا كمصادر منفصلة." chips={['رصيد الخامات', 'استهلاك الوصفات', 'الهالك المسجل', 'فروق الجرد']} /></div>;
}

function ConflictsPage() {
  return <EmptyBusinessPage icon={ClipboardCheck} title="لا توجد تعارضات مفتوحة" text="أي اختلاف بين الكمية المرسلة والمعدودة سيبقى محجوزاً ويظهر هنا لاتخاذ قرار موثق ومتابعة تطبيقه." chips={['جديد', 'بانتظار القرار', 'بانتظار تطبيق الموقع', 'تم الحل']} />;
}

function TeamPage({ users, roles }: { users: User[]; roles: Role[] }) {
  return <div className="page-stack"><section className="stats-grid compact"><StatCard label="المستخدمون" value={String(users.length)} note="حسابات مسمّاة" icon={UsersRound} tone="indigo" /><StatCard label="الأدوار" value={String(roles.length)} note="مجموعات صلاحيات" icon={ShieldCheck} tone="green" /></section><section className="panel table-panel"><PanelHeading title="المستخدمون" subtitle="لا يتم عرض كلمات المرور أو تجزئاتها" /><div className="table-wrap"><table><thead><tr><th>الاسم</th><th>اسم المستخدم</th><th>الدور</th><th>النطاق</th><th>الحالة</th></tr></thead><tbody>{users.map((user) => <tr key={user.id}><td><strong>{user.displayName}</strong></td><td className="ltr">{user.username}</td><td>{user.siteRoles.map((entry) => entry.role.name).join('، ') || 'بدون دور'}</td><td>{user.siteRoles.map((entry) => entry.site?.name || 'كل المواقع').join('، ') || '—'}</td><td><span className={`pill ${user.active ? 'success' : 'neutral'}`}>{user.active ? 'نشط' : 'موقوف'}</span></td></tr>)}</tbody></table></div></section><section className="panel role-list"><PanelHeading title="الأدوار والصلاحيات" subtitle="الصلاحيات الفعلية محفوظة على الخادم" />{roles.map((role) => <div key={role.id}><span className="role-icon"><ShieldCheck /></span><div className="grow"><strong>{role.name}</strong><small>{role.code}</small></div><span>{role.permissions.includes('*') ? 'كل الصلاحيات' : `${role.permissions.length} صلاحيات`}</span></div>)}</section></div>;
}

function OperationsPage({ data }: { data: AdminData }) {
  const [touchInstallers, setTouchInstallers] = useState<Record<SiteType, boolean>>({ BRANCH_TYPE_1: false, BRANCH_TYPE_2: false, KITCHEN: true });
  const profiles: SiteType[] = ['BRANCH_TYPE_1', 'BRANCH_TYPE_2', 'KITCHEN'];
  return <div className="page-stack">
    <section className="panel table-panel"><PanelHeading title="الأجهزة والمزامنة" subtitle="حالة التسجيل وآخر اتصال لكل جهاز" /><div className="table-wrap"><table><thead><tr><th>الجهاز</th><th>الموقع</th><th>الملف</th><th>التسجيل</th><th>آخر ظهور</th><th>الإصدار</th></tr></thead><tbody>{data.devices.map((device) => <tr key={device.id}><td className="ltr">{device.id.slice(0, 8)}</td><td>{data.sites.find((site) => site.id === device.siteId)?.name || 'موقع غير معروف'}</td><td>{siteTypeLabels[device.profile]}</td><td><span className={`pill ${device.enrollmentStatus === 'ENROLLED' ? 'success' : device.enrollmentStatus === 'REVOKED' ? 'danger' : 'warning'}`}>{device.enrollmentStatus === 'ENROLLED' ? 'مسجل' : device.enrollmentStatus === 'REVOKED' ? 'ملغي' : 'بانتظار التسجيل'}</span></td><td>{device.lastSeenAt ? formatDate(device.lastSeenAt) : 'لم يتصل بعد'}</td><td>{device.appVersion || '—'}</td></tr>)}</tbody></table></div>{!data.devices.length && <EmptyState icon={MonitorSmartphone} title="لا توجد أجهزة" text="سيظهر الجهاز بعد إصدار رمز تسجيل لأحد المواقع." />}</section>
    <section className="two-column">
      <article className="panel backup-panel"><PanelHeading title="النسخ الاحتياطي" subtitle="PostgreSQL وسجل الملفات التشغيلية" /><div className="backup-visual"><DatabaseBackup size={34} /><div><strong>لم تُسجل نسخة خارجية بعد</strong><p>يلزم تحديد وجهة VPS مشفّرة وسياسة الاحتفاظ قبل التفعيل.</p></div></div><dl className="detail-list"><div><dt>قاعدة البيانات</dt><dd><span className="status-good">جاهزة</span></dd></div><div><dt>الهدف المقترح</dt><dd>RPO ساعة / RTO ٤ ساعات</dd></div><div><dt>اختبار الاستعادة</dt><dd>بانتظار إعداد الوجهة</dd></div></dl><button className="secondary-button" disabled>تشغيل نسخة الآن</button></article>
      <article className="panel"><PanelHeading title="تقارير الورديات" subtitle="ملفات Excel الأصلية لا تُستبدل" /><EmptyState compact icon={FileSpreadsheet} title="لا توجد تقارير مرفوعة" text="ستظهر ملفات .xlsx بعد إغلاق أول وردية ومزامنتها." /></article>
    </section>
    <section className="panel installers"><PanelHeading title="تطبيقات نقاط التشغيل" subtitle="اختَر نمط الواجهة قبل تنزيل المثبت المناسب" /><div className="installer-grid">{profiles.map((profile) => <article key={profile}><div className="installer-icon"><Download /></div><div><h3>{siteTypeLabels[profile]}</h3><p>Windows x64 · قناة مستقرة</p></div><label className="switch-row"><input type="checkbox" checked={touchInstallers[profile]} onChange={(event) => setTouchInstallers({ ...touchInstallers, [profile]: event.target.checked })} /><span className="switch" /><span>تهيئة شاشة لمس</span></label><button className="secondary-button" disabled><Download size={17} /> الإصدار قيد التجهيز</button></article>)}</div><p className="installer-note"><ShieldCheck size={17} /> المثبتات المنشورة ستكون موقعة، محددة الملف، ولا تحتوي بيانات فرع أو أسرار أو نسخة قاعدة بيانات.</p></section>
  </div>;
}

function EmptyBusinessPage({ icon, title, text, chips }: { icon: LucideIcon; title: string; text: string; chips: string[] }) {
  const Icon = icon;
  return <section className="panel business-empty"><div className="business-empty-icon"><Icon /></div><h2>{title}</h2><p>{text}</p><div className="chip-row">{chips.map((chip) => <span key={chip}>{chip}</span>)}</div><div className="sync-await"><Cloud size={19} /><span>هذه الشاشة جاهزة لاستقبال البيانات من عقد Contract v1</span></div></section>;
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

export default App;
