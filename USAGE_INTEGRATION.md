# Connect your AI usage

BELAC Dock 1.2 connects directly to **Codex, Claude and Cursor**. You do not need OpenTokenUsage running or installed for these native connections.

## Three steps

1. Open **Settings → Usage → Connect AI usage**.
2. Select your providers. Already signed in to their Windows apps? Choose **Next**. Otherwise sign in using the instructions below.
3. Check the results and choose **Show readings on dock**. You can finish with one connected provider and add others later.

Your selection survives restarts. To disconnect providers, uncheck them and use **Save choices without connecting**. Turning off **Enable AI usage readings** stops background polling.

## Your provider

| Provider | What you need | What the dock reads |
| --- | --- | --- |
| Codex | A ChatGPT account signed into Codex on Windows, with file-based authentication | Session and weekly limits where supplied; code review limit where available |
| Claude | A subscription account signed into **Claude Code** on Windows | Session, all-model weekly limits and available model-specific limits |
| Cursor | Signed into the **Cursor desktop app** on Windows | Total, Auto and API usage percentages where supplied |

Official starting points: [Codex](https://chatgpt.com/codex), [Claude](https://claude.ai/download), [Cursor](https://cursor.com/downloads). For Claude Code, follow the [official setup instructions](https://code.claude.com/docs/en/setup) and sign in inside Claude Code.

A website login alone is not enough. There is no password, API key or token to paste into BELAC Dock.

### Supported local sign-ins

- Codex: `%USERPROFILE%\.codex\auth.json`, or the directory specified by `CODEX_HOME`. The legacy `.config\codex` directory is also checked. API-key-only, keyring-only and memory-only authentication are not supported yet. Do not weaken credential storage just to connect the dock; use the optional fallback if it supports your setup.
- Claude Code: `%USERPROFILE%\.claude\.credentials.json`, or the directory specified by `CLAUDE_CONFIG_DIR`.
- Cursor: `%APPDATA%\Cursor\User\globalStorage\state.vscdb`, opened read-only. Cursor can remain open.
- Codex in running WSL distributions is detected as a separate **Codex · WSL (distribution)** entry. Leave **Include Codex in running WSL distributions** enabled in setup. The default WSL user’s `.codex/auth.json` or `.config/codex/auth.json` is read; `CODEX_HOME` is honored when available to the non-login WSL process. Custom shell-only environment settings, keyring-only WSL sessions, additional users, WSL Claude and custom Cursor profiles are not detected.
- The dock queries running distributions only and excludes Docker’s internal distributions. It does not intentionally start stopped distributions. A previously fetched WSL reading stays marked stale while its distribution is unavailable.

The dock never modifies these files or rotates refresh tokens. If a session expires, open the provider app to renew the sign-in. The next check reads updated credentials automatically.

## Refresh and status

The dock polls while it runs. Requests are limited to once every two minutes for Codex/Cursor and five minutes for Claude. Repeatedly clicking Refresh respects these intervals. Provider rate limits delay requests until the retry time.

An unavailable provider does not stop others working. Previous readings are marked stale on failure. Missing limits are not displayed as zero. Changed accounts do not reuse the previous account's cached readings.

| What you see | What to do |
| --- | --- |
| Sign in to the provider | Open its Windows app and sign in. Browser-only sessions are not detected. For WSL Codex, start its distribution and sign in there. |
| Sign-in expired or access denied | Open the provider app and renew sign-in. Some accounts may not have access to usage. |
| Couldn't read sign-in | Let the app finish signing in, then retry. Use the same Windows user. |
| Rate limit reached | Wait until the displayed retry time. Other monitors share your provider's limits. |
| No supported usage limits | Check the provider dashboard. Some plans, including Cursor Enterprise/request-based plans, use unsupported payloads. |
| Couldn't refresh | Check your internet connection. The dock retries automatically. |

## Optional OpenTokenUsage fallback

For other providers, expand **Other providers / optional fallback** and enable **Also read OpenTokenUsage when it is running**. Install it from its [official releases](https://github.com/PowerUserZ/OpenTokenUsage/releases/latest) if needed and connect the additional providers there.

Fallback is off by default and uses `http://127.0.0.1:6736/v1/usage`. Successful native readings take priority. Only fallback readings require the companion to remain running.

## Privacy and implementation

Access tokens are read locally and held in memory. Each token is sent only to its provider's fixed HTTPS endpoint, with redirects disabled. Tokens and raw responses are never saved to dock settings or logs. No analytics are collected.

These usage endpoints are undocumented and may change. Mappings are adapted from MIT-licensed [OpenTokenUsage](https://github.com/PowerUserZ/OpenTokenUsage); see [third-party notices](THIRD_PARTY_NOTICES.md). Native readers provide usage limits, not the companion's full spend-history, multi-account or credit-management features.
