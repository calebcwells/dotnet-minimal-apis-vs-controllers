# Security Policy

## Demonstration code

This repository is an educational comparison, not a production starter or deployable security
reference. The API keys `speaker-key` and `reviewer-key` are public, intentionally hard-coded test
fixtures. They are not secrets and must not be reused in a real system.

Production applications should use an appropriate authentication scheme such as OAuth or OpenID
Connect. If API keys are required, store only protected key material and support rotation,
revocation, and constant-time verification.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting feature for security issues. Do not open a
public issue containing exploit details, credentials, tokens, or other sensitive information.
