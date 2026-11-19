# Kitchen password enrollment

Accepted by the owner for local Phase 2 implementation on 18 November 2026.

Admin selects one active Kitchen site as the destination for new password enrollments. This removes the site picker from the desktop and gives the server an unambiguous site even when several kitchens exist. Saving the form replaces the destination and password for future setups. Only the Argon2id verifier, site, last administrator ID and change time are stored in the central setting.

The new HTTPS endpoint uses the existing device creation transaction, single-writer constraint, credential generation and sync state. The transaction locks the verified password setting and active site through device issuance. The desktop stores the issued credential using Windows current-user protection. Setup always needs a connection. Previously enrolled devices keep their identity and offline data.

Rotating the password leaves issued credentials valid. Revocation invalidates a specific device credential. Rotate the enrollment password as well if the person being removed knows it. If a first connection is interrupted after central issuance and before the local credential is saved, an administrator can revoke that unused device and retry setup. Earlier clients retain their one-time-code enrollment route.
