# MikuMikuLoader.App

The desktop loader client (Avalonia, .NET 8) for Gorilla Tag BepInEx mods.

> Written but not compiled in the author's tooling — run it and report anything
> that doesn't build. Avalonia XAML is fussier than plain C#, so expect a tweak or two.

## Run it

Requires the .NET 8 SDK. Start the server first, then:

```bash
cd MikuMikuLoader.App
dotnet run
```

## What works now

- **Custom window** — dark/orange/monospace, custom title bar (min/max/close + drag).
- **Browse Online** — live search, sort, **Trusted only** toggle, tags. Each card:
  **Install** (one-click), **Details**, rating, GT-compat badge.
- **One-click install** — downloads a mod's DLL into `<Gorilla Tag>/BepInEx/plugins`;
  button flips to **Installed** / **Update**.
- **Library** — installed mods with Uninstall / Open plugins; auto-detect game folder.
- **Settings** — server URL + Gorilla Tag folder (Steam auto-detect or Browse…).
- **Accounts** — Sign in / create account. Registering makes a **User** (comment +
  rate); ticking "register as developer" makes a **Developer**. Token is remembered
  across launches.
- **Developer panel** (Dev/Owner only, shown as an extra nav tab) —
  become-a-developer, apply for trusted, **upload a mod** (file picker + metadata +
  tags), and manage your mods (edit version/description/GT version/tags, delete).
- **Mod details** — description, tags, ratings, and comments. Signed-in users can
  **rate** (1–5 + optional review) and **comment**; guests get a sign-in prompt.
- **Update check** — compares to the latest release at
  `github.com/cfm-miku-en/MikuMikuLoader/releases`; shows a title-bar pill if newer.

## Roles

Registering creates a **User** (comment + rate). Opt into **Developer** to upload
mods. Dev/Owner see the **Developer** tab. Owner-only actions (moderation,
announcements, preset tags, bans) live with the server, not in the loader.

## Structure

- `Services/` — `ApiClient` (auth + mods + comments + reviews), `SessionService`
  (sign-in state + token), `SettingsService`, `GameLocatorService`,
  `InstalledModsService`, `InstallService`, `UpdateService`.
- `ViewModels/` — MVVM (CommunityToolkit.Mvvm).
- `Views/` — window + Browse / Library / Settings / Developer / Login / Mod detail.
- `Models/ApiModels.cs` — mirrors the server's JSON.

## Next up

- Owner admin page served by the server (owner tooling ships server-side).
- Owner admin page served by the server.
