# Quickstart: validating stronger PIN & passphrase protection

Prerequisites: a built wallet (`dotnet run --project src/DigiByte.Web`), a Chromium browser and a second browser without PRF support for the fallback checks, DevTools Application tab to inspect IndexedDB. Use test wallets only.

## Automated

```bash
dotnet test tests/DigiByte.Wallet.Tests
node --test tests/js
```

Expected: all green; the new envelope, migration, policy, throttle, and biometric tests exist and failed before the change (constitution III). The JS suite reads the fixtures described in [contracts/envelope-format.md](contracts/envelope-format.md).

## Scenarios

1. **New passphrase wallet (US1)**. Create a wallet, choose passphrase, try `password1234` (rejected with a reason), then a valid one. Lock and unlock. In IndexedDB confirm only `wallet_seed_v2_{id}` exists and starts with `dgb2$`.
2. **Automatic migration (US2, SC-001)**. Load a profile created by the previous build (fixture) with a PIN wallet. Unlock. Confirm `wallet_seed_v2_{id}` and `wallet_migration_{id}` now exist, `wallet_seed_{id}` is unchanged byte for byte, and the recovery phrase and addresses match.
3. **Interrupted migration (SC-004)**. Use the test hook that throws after each of the five migration steps; after each, reload and unlock with the same PIN. Expected: always unlocks; migration completes on a later unlock.
4. **Rollback**. After scenario 2, start the previous build against the same profile. Expected: unlocks from the legacy copy. Change the PIN in the old build, return to the new build: it detects the fingerprint mismatch, unlocks with the new PIN, and re-migrates.
5. **Legacy removal**. Advance the clock 8 days (test hook). Unlock. Expected: legacy key deleted, record shows `legacyRemovedAt`. Change the PIN on another wallet: legacy copy is deleted immediately.
6. **Recovery (US3, SC-005)**. For legacy, migrated, PIN, and passphrase wallets: use the recovery flow with the correct phrase (works, old credential stops working) and with a wrong phrase (nothing changes).
7. **Throttle (US5, SC-007)**. Enter 3 wrong credentials on Unlock and PsbtSign for a PIN wallet and a passphrase wallet. Expected: same delays, messages say "PIN" or "passphrase". Force an allocation failure (test hook): not counted as a wrong attempt.
8. **Biometrics (SC-011)**. On a PRF browser, enrol, then copy IndexedDB to another profile: the seed cannot be decrypted. Migrate a wallet with biometrics already enrolled: prompted once to re-enrol, `bio_key` removed. On a non-PRF browser: warning shown at enrolment and once for existing users.
9. **Low-end phone (US4, SC-003)**. On the reference phone (or Chrome DevTools CPU 6x slowdown plus a memory-limited emulation) time 20 unlocks per profile. Expected: typical at most 3 s, worst case at most 6 s, spinner animates, double taps ignored. Record results; freeze the profile parameters.
10. **Unsupported browser (SC-013)**. Disable WebAssembly (browser flag). Create a wallet: warning and explicit agreement required; wallet marked legacy protection; opening it on a capable browser upgrades it.
11. **Offline**. Load the app, go offline, unlock and migrate. Expected: works; the vendored script, worker, and deny list are served from the service worker cache.
12. **Wording (US6, SC-008)**. Switch between English and Spanish on every changed screen. Expected: no raw keys, no untranslated text; `create.pin_limitation` reworded to match the protection actually in place.
13. **CSP**. Open the console during all scenarios. Expected: no CSP violations.

## Benchmark (feeds SC-002 and SC-003)

Run the benchmark script from the tasks (derivation time and peak memory for each profile, on the reference phone, a mid-range phone, and desktop). Compare per-guess cost to the legacy PBKDF2 setting on CPU and, where available, with published GPU rates. Record in `research.md` and revisit the parameters or SC-002 if the 100x target is not met (plan, Decisions taken).
