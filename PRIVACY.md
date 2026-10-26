# Privacy Policy

**My Tool Box** · Effective date: 24 October 2026

My Tool Box is a desktop application for Windows developed by Tanbin
([www.magestica.com](https://www.magestica.com/)). This policy explains what information
the app handles and what it does with it.

## Summary

- My Tool Box **does not collect, store, sell or share any personal data**.
- It has **no accounts, no analytics, no telemetry, no ads and no tracking**.
- Everything it does happens **on your own computer**. Nothing is uploaded to the
  developer or to any third party.

## How the app works

My Tool Box is a native Windows app (WinUI 3). It has no web server and no embedded browser:
it opens no listening network port, and closing the window quits the app.

## Information the app reads on your computer

Depending on which tool you use, My Tool Box reads information **locally** and shows it
to you. None of it leaves your computer, and it is not saved after you close the app.

| Tool | What it reads |
|---|---|
| Folder Size | Names, sizes and dates of files and folders you choose to scan |
| Disk info | Drive model, serial number, firmware, capacity and health (S.M.A.R.T.) data |
| Hash, Encrypt / decrypt, Encoders, JWT, JSON and other text tools | Only the text or file you enter or select |

Files are only changed when you explicitly delete them with a confirmation
(moved to the Recycle Bin or deleted permanently).

## Network activity

My Tool Box does **not** contact the developer or any online service on its own.
It does not check for updates and does not send usage or crash reports.

Network traffic happens **only when you use a network tool**, and only towards the
addresses you type in:

- **Ping, IP scanner, Telnet / ports**: send ICMP echo requests, ARP requests and
  TCP connections to the hosts, ranges and ports you enter.
- **Host names**: when you enter a host name, or when the IP scanner looks up
  device names, the request goes to the DNS server your computer is already
  configured to use.

Only scan networks and devices you own or are allowed to test.

## Information stored by the app

My Tool Box saves two small files on your computer, in `%LocalAppData%\MyToolBox`:

- `usage.json`: which tools you opened recently and how often, used for the Recent and Most used lists on the Home page.
- `env-backups\`: a copy of your environment variables, made just before you change one, so a mistake can be undone by hand.
  The newest 30 are kept.

Hosts file edits also leave a backup next to the hosts file (`hosts.bak-<date>`). Nothing here contains personal data, none
of it is transmitted, and all of it can be deleted at any time. If the app hits an error it writes the details to
`%TEMP%\toolbox-crash.log`; that file stays on your PC and is never sent anywhere.

## Links to other websites

The Home and About pages link to GitHub and www.magestica.com. Those links open in your web
browser, and those websites have their own privacy policies.

## Children's privacy

My Tool Box does not knowingly collect any information from anyone, including children
under 13.

## Changes to this policy

If this policy changes, the updated version will be published at
<https://github.com/smtanbin/tool-box/blob/main/PRIVACY.md> with a new effective date.

## Contact

Questions about this policy: open an issue at
<https://github.com/smtanbin/tool-box/issues> or contact
[www.magestica.com](https://www.magestica.com/).
