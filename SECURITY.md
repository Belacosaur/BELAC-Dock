# Security and privacy

The dock runs with the current user's permissions. Dock-only changes are opt-in and journaled for recovery. The compatibility fallback changes window styles and may remove Alt+Tab entries. Elevated/protected apps can remain unsupported.

Native usage reads existing Codex/Claude Code credential files and Cursor's local SQLite sign-in store in read-only mode. Access tokens are held in memory and sent only to their fixed HTTPS provider endpoint; redirects are disabled. The dock does not refresh or modify provider credentials, store tokens in its settings, or log request headers/response bodies. Optional OpenTokenUsage fallback uses only http://127.0.0.1:6736. The dock does not collect analytics. Settings and diagnostic exports may include private executable paths, window titles and app names. Review and redact them before sharing. Do not attach a live recovery journal or raw configuration to a public issue.

Use the hosting repository's private vulnerability reporting feature if enabled; otherwise contact its maintainer privately before publishing exploit details. Maintainers should enable private vulnerability reporting before opening the repository. Only current releases are maintained. Archives are unsigned unless explicitly stated otherwise.
