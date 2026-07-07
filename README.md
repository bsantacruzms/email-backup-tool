# Email Backup Tool

Back up any e-mail account to **native Outlook `.msg` files**, packaged into a single dated
zip named `email@example.com_YYYY-MM-DD.zip` — no Microsoft Outlook required.

Give it your address and password, and the tool auto-detects the server settings (IMAP/POP3),
validates the connection, downloads every message from every folder, and writes each one as a
`.msg` file you can open or import into Outlook later.

- **Portable** — ships as a single self-contained `.exe` (no .NET runtime to install).
- **No Outlook dependency** — `.msg` files are generated directly from the raw MIME.
- **Auto-detects** IMAP/POP settings for Gmail, Microsoft 365 / Outlook.com, Yahoo, iCloud,
  and most other providers; manual override is always available.
- **Full fidelity** — bodies (text + HTML), attachments, inline images and headers are preserved.
- **Desktop app _and_ command line** — use whichever you prefer.

---

## Download

Grab the latest portable executables from the [Releases](../../releases) page:

| File | What it is |
|------|------------|
| `EmailBackupTool-<version>-portable.exe` | Desktop app (double-click to run) |
| `ebk-<version>-portable.exe` | Command-line tool for scripts/automation |

Both run on Windows 10/11 x64 with nothing else installed.

---

## Desktop app

1. Run `EmailBackupTool-<version>-portable.exe`.
2. Enter your **email address** and **password / app password**.
3. Click **Detect settings** (or just **Start backup** — it will detect automatically).
4. Optionally pick an output folder and click **Start backup**.

When it finishes you get, in the output folder:

```
user@example.com_2026-07-06/        <- one subfolder per mail folder, full of .msg files
user@example.com_2026-07-06.zip     <- the same thing, zipped into one file
```

## Command line (`ebk`)

```text
ebk detect --email <address>
ebk backup --email <address> [options]
```

Examples:

```powershell
# See which server settings would be used (no login required)
ebk detect --email me@example.com

# Back up to the current folder (prompts for password)
ebk backup --email me@example.com

# Back up to a specific folder
ebk backup --email me@example.com --out D:\Backups

# Force manual server settings (skips auto-detect)
ebk backup --email me@corp.com --host mail.corp.com --port 993 --security ssl
```

Key options: `--password`, `--user`, `--out`, `--protocol imap|pop3`,
`--host`, `--port`, `--security ssl|starttls|none|auto`, `--no-zip`, `--no-keep`.
Run `ebk help` for the full list.

---

## App passwords (important)

Most major providers block your normal password over IMAP/POP and require an **app password**:

- **Gmail / Google Workspace** — enable 2-Step Verification, then create an
  [App Password](https://myaccount.google.com/apppasswords).
- **Microsoft 365 / Outlook.com** — create an
  [App Password](https://account.microsoft.com/security) (requires 2FA).
- **Yahoo / AOL** — Account Security → **Generate app password**.
- **iCloud** — [Sign-in & Security → App-Specific Passwords](https://support.apple.com/en-us/102654).

Use the app password in place of your normal password.

---

## How auto-detection works

Settings are resolved in layers, best result first:

1. Built-in table of common providers.
2. Mozilla / ISP **autoconfig** (`autoconfig.thunderbird.net` and the domain's own config).
3. DNS **SRV** records (`_imaps._tcp`, `_imap._tcp`, `_pop3s._tcp`, …).
4. Common host-name guesses (`imap.<domain>`, `mail.<domain>`, …).

Each candidate is then probed with a live TLS connection, and the first one that answers is used.
IMAP is preferred over POP3 because it captures the full folder structure.

---

## Restoring into Outlook

1. Unzip the backup.
2. **Double-click** any `.msg` to open it, **or** drag one or more `.msg` files (or a whole
   folder) into an Outlook folder to import them.

The `.msg` format is Outlook's native single-message format, so no conversion is needed.

---

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build EmailBackup.sln -c Release
dotnet test tests/EmailBackup.Core.Tests/EmailBackup.Core.Tests.csproj

# Produce the portable executables in dist/
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

## Project structure

| Project | Description |
|---------|-------------|
| `src/EmailBackup.Core` | Engine: auto-discovery, IMAP/POP backup, `.msg` export, zip packaging |
| `src/EmailBackup.App` | WPF desktop app (portable single-file exe) |
| `src/EmailBackup.Cli` | `ebk` command-line tool |
| `tests/EmailBackup.Core.Tests` | xUnit test suite |

## Built with

- [MailKit](https://github.com/jstedfast/MailKit) (MIT) — IMAP/POP client
- [MsgKit](https://github.com/Sicos1977/MsgKit) (MIT) — writes Outlook `.msg` files without Outlook
- [DnsClient.NET](https://github.com/MichaCo/DnsClient.NET) (Apache-2.0) — DNS SRV lookups

## License

[MIT](LICENSE)

---

> **Note:** Your credentials are used only to connect to your mail server and are never stored or
> transmitted anywhere else. Backups are written only to the local folder you choose.
