# Client store notes

- **Bluetooth multiplayer: Android only** (hidden on iOS / PC / WebGL / Editor).
- Soft currency comes from tournaments, IAP, and rewarded ads — not from normal match wins.
- Crash reporting: `CrashReporter` → API; wire Firebase Crashlytics or Sentry for production builds.
- Client sends `X-Client-Version` (`ClientGate.ClientVersion`) on every API call.
