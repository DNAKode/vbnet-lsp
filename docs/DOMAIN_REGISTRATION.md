# Marketplace Domain Verification Notes

Source: VS Code extension publishing guidance on eligible domains.

## Key requirements (summary)
- You must control the DNS for the domain you claim.
- Verification is done by adding a DNS TXT record with the provided token.
- The domain must be a top‑level domain or subdomain you control (not a shared provider domain).
- The domain must be reachable over HTTPS.
- The verification check expects an HTTP 200 response to a HEAD request.

## Next steps (when ready)
1) Decide which domain to use for the publisher (e.g., `dnakode.com`).
2) Add the TXT record in DNS when Azure DevOps prompts for verification.
3) Ensure the domain responds over HTTPS with HTTP 200 to HEAD.

Note: Per current plan, start the verification process on July 13, 2026 (six months from January 13, 2026).
