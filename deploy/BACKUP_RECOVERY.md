# PostgreSQL backup and recovery

Run `sh deploy/scripts/backup-postgres.sh` on the VPS. Archives are timestamped UTC, mode 600, checked by `pg_restore --list`, and accompanied by SHA-256. Default retention is 14 days. Set `SUGAR_BACKUP_DIR` and `SUGAR_BACKUP_RETENTION_DAYS` in the service environment to change these.

Install the units in `deploy/systemd` under `/etc/systemd/system`, run `systemctl daemon-reload`, then `systemctl enable --now sugar-postgres-backup.timer`. Inspect failures using `journalctl -u sugar-postgres-backup.service` and inspect `backups/postgres/status.json` for the last successful run.

Run `sh deploy/scripts/verify-backup-restore.sh /absolute/path/to/backup.dump` monthly and after schema changes. It restores into a newly named isolated database, verifies migrations/sites/events are readable, and removes that database. It never restores over the operational database.

For disaster recovery, stop API writers, provision a separate PostgreSQL 17 instance, restore the selected archive using `pg_restore --exit-on-error --no-owner`, compare event positions and business totals, and only then change the API database connection. Never restore an old archive over new business transactions. Keep the displaced database until reconciliation is complete.

Same-VPS archives do not survive VPS loss. An encrypted off-VPS destination and its credentials are required for that protection. Supply that destination before relying on this setup as the business's sole recovery copy.
