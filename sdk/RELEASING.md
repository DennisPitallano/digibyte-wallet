# Releasing the DigiPay SDKs

One runbook for every language SDK under `sdk/`. The pattern is the same: bump version → commit → tag → push, and a GitHub Actions workflow picks it up from there.

## Prerequisites (one-time per language)

### Node (`@dgbwallet/digipay` on npm)

1. Own the `@dgbwallet` npm org (or migrate the package to another scope in `sdk/node/package.json`).
2. Generate an **Automation** token: npmjs.com → Profile → Access Tokens → Generate New → Automation.
3. Repo Settings → Secrets and variables → Actions → New repository secret:
   - Name: `NPM_TOKEN`
   - Value: `npm_…`
4. In the org settings on npmjs.com, turn on **require 2FA for package publish = auth-only** (tokens can publish, TOTP required for UI).

### Python (`digipay` on PyPI)

1. Own the `digipay` name on PyPI (project needs to exist with you as owner).
2. On PyPI: Project → Publishing → **Add a pending publisher**:
   - Owner: `DennisPitallano`
   - Repository: `digibyte-wallet`
   - Workflow: `publish-sdk-python.yml`
   - Environment: `pypi`
3. Repo Settings → Environments → `pypi` (protect with required reviewers if you want a pause-to-confirm step).

No token needed — PyPI trusted publishing uses GitHub's OIDC.

## Cutting a release

Pick the next version following [SemVer](https://semver.org/):

- `patch` — bug fixes, docs (`0.1.0` → `0.1.1`)
- `minor` — new endpoints, new optional params (`0.1.0` → `0.2.0`)
- `major` — breaking changes to existing signatures (`0.1.0` → `1.0.0`)

### Node

```bash
cd sdk/node

# 1. Bump version — writes package.json AND package-lock.json AND creates a git tag locally.
#    Use `--no-git-tag-version` if you want to defer the tag; we prefer tagging in this workflow.
npm version patch --no-git-tag-version    # → 0.1.1

# 2. (optional) Update the fallback `SDK_VERSION` constant in src/client.ts & src/http.ts
#    — they're what the SDK sends in the User-Agent header; out-of-sync is cosmetic, not broken.

# 3. Commit + tag with the matching tag the workflow watches for.
cd ../..
git add sdk/node/package.json sdk/node/package-lock.json sdk/node/src
git commit -m "chore(sdk-node): release 0.1.1"
git tag sdk-node-v0.1.1
git push origin main sdk-node-v0.1.1
```

The `publish-sdk-node` workflow runs: checkout → npm ci → build → test → `npm publish --access public --provenance`. Failed version-match = workflow bails before publishing.

Watch the Actions tab until the job is green, then:

```bash
npm view @dgbwallet/digipay versions
```

### Python

```bash
cd sdk/python

# 1. Bump version — single source of truth is pyproject.toml.
#    Also bump the `__version__` in src/digipay/__init__.py.

# 2. Commit + tag.
cd ../..
git add sdk/python/pyproject.toml sdk/python/src/digipay/__init__.py
git commit -m "chore(sdk-python): release 0.1.1"
git tag sdk-python-v0.1.1
git push origin main sdk-python-v0.1.1
```

The `publish-sdk-python` workflow runs: checkout → install build tool → `python -m build` → `twine upload` via trusted publishing.

Watch the Actions tab; when green:

```bash
pip index versions digipay
```

## Rollback

npm + PyPI are both append-only. "Rollback" means publishing a new patch that reverts the change:

```bash
cd sdk/node
npm version patch --no-git-tag-version    # → 0.1.2
# … revert the offending code …
git tag sdk-node-v0.1.2
git push origin main sdk-node-v0.1.2
```

`npm unpublish` and PyPI's yank exist but are heavily discouraged — every downstream lockfile will break.

## Trying before release

```bash
# Node
cd sdk/node
npm pack                         # dgbwallet-digipay-0.1.0.tgz
cd /tmp/my-test-app
npm install ~/digibyte-wallet/sdk/node/dgbwallet-digipay-0.1.0.tgz

# Python
cd sdk/python
python -m build                  # dist/digipay-0.1.0-py3-none-any.whl
cd /tmp/my-test-app
pip install ~/digibyte-wallet/sdk/python/dist/digipay-0.1.0-py3-none-any.whl
```
