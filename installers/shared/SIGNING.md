# Release signing

Use a legitimate Authenticode code-signing certificate installed in the Windows build account's certificate store. The certificate's private key must remain in the certificate provider or hardware token; never copy it into this repository, staging directory, installer, or desktop settings.

Set `SUGAR_SIGN_CERT_THUMBPRINT` and, if necessary, `SUGAR_SIGNTOOL_PATH`. `SUGAR_SIGN_TIMESTAMP_URL` can override the default DigiCert RFC 3161 timestamp service. Run each product's `build-installer.ps1 -Production -Version x.y.z`.

The scripts sign the application's EXE before packaging, then sign and verify the completed installer with SHA-256 and timestamping. Missing signing credentials make a production build fail. Builds without `-Production` can produce explicitly unsigned development artifacts for testing; they must not be promoted as signed production releases.

A purchased or organization-provided signing certificate is an external dependency. Signing and HTTPS do not guarantee that SmartScreen or browser reputation warnings disappear. Do not disable browser or Windows protection to distribute releases.
