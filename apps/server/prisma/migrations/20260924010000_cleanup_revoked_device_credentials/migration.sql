-- Revoked devices retain their historical rows, but no authentication or
-- writer-ownership material. This also cleans devices revoked by older builds.
UPDATE devices
SET credential_hash = NULL,
    key_thumbprint = NULL,
    active_writer = FALSE
WHERE enrollment_status = 'REVOKED'
  AND (credential_hash IS NOT NULL OR key_thumbprint IS NOT NULL OR active_writer = TRUE);
