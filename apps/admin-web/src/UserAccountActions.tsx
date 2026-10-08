import { useState, type FormEvent } from 'react';
import { friendlyError, type Role, type Site, type User } from './api';

export interface UserAccountActionProps {
  currentUserId: string;
  onApproveUser: (id: string, input: { roleId: string; siteId?: string }) => Promise<void>;
  onDeleteUser: (id: string, confirmation: string, reject: boolean) => Promise<void>;
}

export function UserAccountActions({ user, roles, sites, currentUserId, onApproveUser, onDeleteUser }: UserAccountActionProps & { user: User; roles: Role[]; sites: Site[] }) {
  const [action, setAction] = useState<'approve' | 'delete' | null>(null);
  const [confirmation, setConfirmation] = useState('');
  const [roleId, setRoleId] = useState('');
  const [siteId, setSiteId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const pending = user.status === 'PENDING_PERMISSION';

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError('');
    try {
      if (action === 'approve') await onApproveUser(user.id, { roleId, ...(siteId ? { siteId } : {}) });
      else await onDeleteUser(user.id, confirmation, pending);
      setAction(null);
      setConfirmation('');
    } catch (caught) {
      setError(friendlyError(caught));
    } finally {
      setBusy(false);
    }
  };

  return <div className="user-account-actions">
    <div className="row-actions">
      {pending && <button type="button" disabled={busy} onClick={() => { setAction('approve'); setError(''); }}>موافقة</button>}
      <button type="button" className="confirm-delete" disabled={busy || !currentUserId || user.id === currentUserId} onClick={() => { setAction('delete'); setConfirmation(''); setError(''); }}>{pending ? 'رفض' : 'حذف نهائي'}</button>
    </div>
    {action && <form className="account-confirmation" onSubmit={submit} aria-label={action === 'approve' ? `الموافقة على ${user.username}` : `حذف ${user.username}`}>
      {action === 'approve' ? <>
        <strong>الموافقة على <bdi>{user.username}</bdi></strong>
        <label>الدور<select required value={roleId} disabled={busy} onChange={(event) => setRoleId(event.target.value)}><option value="">اختر دوراً</option>{roles.filter((role) => role.active).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}</select></label>
        <label>نطاق الموقع<select value={siteId} disabled={busy} onChange={(event) => setSiteId(event.target.value)}><option value="">كل المواقع</option>{sites.filter((site) => site.active).map((site) => <option key={site.id} value={site.id}>{site.name}</option>)}</select></label>
      </> : <>
        <strong>{pending ? 'رفض الطلب وحذف الحساب نهائياً' : 'حذف الحساب نهائياً'}</strong>
        <p>لا يمكن التراجع. يُحذف الحساب وصلاحياته، وتبقى سجلات العمليات السابقة لأغراض المراجعة.</p>
        <label>اكتب اسم المستخدم <bdi>{user.username}</bdi> للتأكيد<input autoComplete="off" value={confirmation} disabled={busy} onChange={(event) => setConfirmation(event.target.value)} required maxLength={100} /></label>
      </>}
      {error && <div className="form-error" role="alert">{error}</div>}
      <div className="form-actions"><button className="primary-button" disabled={busy || (action === 'delete' ? confirmation !== user.username : !roleId)}>{busy ? 'جارٍ الحفظ…' : action === 'approve' ? 'تأكيد الموافقة' : 'تأكيد الحذف النهائي'}</button><button type="button" className="secondary-button" disabled={busy} onClick={() => { setAction(null); setConfirmation(''); }}>إلغاء</button></div>
    </form>}
  </div>;
}
