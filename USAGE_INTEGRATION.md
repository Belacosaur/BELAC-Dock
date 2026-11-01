# Connect your AI usage

BELAC Dock shows your AI usage at the bottom of the taskbar. A separate, free companion app called **OpenTokenUsage** connects to your providers and supplies the readings.

You do not need to write code, configure a server, or give BELAC Dock your passwords or API keys. Your app shortcuts work even if you skip AI setup.

## Start the guided setup

Open **BELAC Dock → Settings → Usage → Connect AI usage**. You can also click the connect icon at the bottom of an empty usage rail, or choose the optional setup during your first launch.

The wizard has three steps. You can go back or select **Set up later** at any time.

## 1. Install and open OpenTokenUsage

1. In the wizard, select **Download OpenTokenUsage**. This opens its [official release page](https://github.com/PowerUserZ/OpenTokenUsage/releases/latest).
2. Download the Windows **x64 setup executable** from the release assets, not the source-code ZIP.
3. Run the setup file and open OpenTokenUsage. You choose whether to install it; BELAC Dock does not install anything automatically.
4. Return to the wizard and select **Next**.

Already have it? Select **Open OpenTokenUsage**, or search for it in the Windows Start menu. It may open in the notification area beside the clock; check the hidden-icons arrow too.

For people who prefer a terminal, the project's documented alternative is:

```powershell
winget install PowerUserZ.OpenTokenUsage
```

## 2. Connect your providers in the companion

Open OpenTokenUsage, enable the providers you use, and follow the setup or sign-in guidance shown for each one. Refresh its panel until you can see your usage there.

| Provider | Where to start |
| :--- | :--- |
| Codex | Make sure your Codex coding tool is signed into the intended account. Enable Codex in OpenTokenUsage and follow any missing-login guidance it shows. A ChatGPT website login alone may not be sufficient. |
| Claude | Make sure the Claude coding tool/account you want to track is signed in. Enable Claude and follow the companion's setup guidance. Browser chat and coding-tool authentication are not necessarily interchangeable. |
| Cursor | Sign into the account you use in Cursor, enable Cursor in OpenTokenUsage, and follow its provider-specific instructions if usage is unavailable. |
| Copilot or another provider | Enable the provider in OpenTokenUsage and follow its instructions. Supported account types and setup steps vary. |

Use **Help for your provider** in the wizard for the upstream instructions, or visit the [supported-provider list](https://github.com/PowerUserZ/OpenTokenUsage#supported-providers).

Complete sign-ins in the appropriate app or browser. **Do not paste passwords, tokens, cookies or credential-file contents into BELAC Dock, an AI chat, or a public issue.** BELAC Dock has no credential-entry fields. OpenTokenUsage handles provider connections separately.

## 3. Check and show readings

Return to the wizard and select **Next**. It checks the local connection and lists the provider readings it received.

- **Readings received:** select **Show readings on dock**. This enables usage and makes those providers visible, including any you previously hid.
- **Connected — waiting for readings:** the connection works, but there is no usable provider data yet. Finish setup and refresh in OpenTokenUsage, then choose **Check again**.
- **No connection yet:** open OpenTokenUsage and leave it running. If it is already running, restart it, wait for its panel to load, and check again.

You can finish with one provider and connect more later. A provider missing from the list has not supplied data to the dock yet; installing its app alone does not guarantee it will appear.

The check reads cached usage from OpenTokenUsage. It does not prove your provider login is still valid or request fresh quota data directly. Check the displayed update time; refresh in OpenTokenUsage if you need a newer reading.

## Keep it working

Keep OpenTokenUsage running in the tray while you use the dock. If you want both apps available after restarting Windows, enable their start-at-sign-in options separately. Closing the companion stops new readings; the dock can retain older readings marked stale.

In **Settings → Usage**, you can hide or reorder providers and change how often BELAC Dock checks for updates. This changes the dock's polling interval, not the companion's provider-refresh schedule.

## Common problems

| What you see | What to try |
| :--- | :--- |
| Companion not detected, but you installed it | Open it from Windows Start. Detection can miss portable/custom installations; a working local connection can still be used. |
| Provider missing or needs attention | Enable and refresh it in OpenTokenUsage. Resolve its account/sign-in error there first. |
| Usage is visible in the companion but the dock cannot connect | Restart the companion. Its local API normally starts automatically; another program using port 6736 can prevent it from starting. See the [upstream local API notes](https://github.com/PowerUserZ/OpenTokenUsage/blob/main/docs/local-http-api.md). Do not open firewall ports or expose it to your network. |
| Wrong account or old readings | Check the selected account and refresh time in OpenTokenUsage. BELAC Dock displays the data it receives. |
| Reading exists but no percentage ring | Some providers supply text or credits without a percentage. The dock does not invent quota percentages. |
| Still stuck | Open a [BELAC Dock issue](https://github.com/Belacosaur/BELAC-Dock/issues) with your Windows/app versions and the wizard message. Redact account details and paths from screenshots. Provider login issues belong with OpenTokenUsage. |

## For developers

BELAC Dock reads `GET http://127.0.0.1:6736/v1/usage`. OpenTokenUsage documents this as a cached, read-only list for enabled providers; a successful response can be an empty array. No provider refresh or credential lookup is performed by the dock. The client rejects redirects, bypasses proxies, limits the response body to 1 MB and uses a five-second deadline.

See the [upstream API contract](https://github.com/PowerUserZ/OpenTokenUsage/blob/main/docs/local-http-api.md) and [BELAC Dock architecture](ARCHITECTURE.md). Upstream provider support and login requirements can change independently of BELAC Dock.
