# The Windows installer (installer plan, section 1; card 1242-A)

The design for one Windows installer that holds the whole kit, built by `bin\publish-release.ps1` and attached to
each GitHub release in place of `ProjectToolkit-win-x64.zip`. The plan is
[`jobs/plans/installer_plan.md`](../../../jobs/plans/installer_plan.md); its sections 2-4 are carded from the
[card list](#card-list-for-plan-sections-2-4) at the end. The developer answered the five
[questions](#decisions-by-the-developer) on 2026-11-24; the rules below follow those answers. Rules are INS-1 to INS-24.
It records the code as of 2026-11-24.

## How the kit works today

| What | Where it is, and how it is found | Code |
|---|---|---|
| The kit (templates) | A project's `toolkit` setting (relative to its repository allowed), else the pool file's `toolkit` (1193-A), else the nearest folder **above the exe** that has `assets\templates\`. | `Scheduler.Host/Templates/ToolkitLocator.cs`; `Program/ProjectParts.cs` `ToolkitSetting`, ~line 218 |
| Template refresh (540-B) | Treats the kit as a **git repository**: fetches it each tick, compares each generic file with the template on the kit's `origin/main`, and calls a project copy "unedited" when its blob id is a past version of the template in the kit's history. With no `.git` it still runs a fetch each tick, then logs "no origin/main in the toolkit" once and keeps nothing in step. | `Templates/TemplateRefresh.cs` (`Step`, `FetchToolkit`, `PastVersions`) |
| `projecttoolkit.ps1` | `$Kit = Split-Path -Parent $PSScriptRoot`, templates at `$Kit\assets\templates`. Reads the kit only; writes the target repository and `%LOCALAPPDATA%\ProjectToolkit\pool.json` (creates it, or splices a project in). | `bin/projecttoolkit.ps1` lines 102-103, `Join-Pool` |
| Program state | Everything under `%LOCALAPPDATA%`: the pool's log and state in `ProjectToolkit\pool\` (MP-C3), the pool file `ProjectToolkit\pool.json`, a single project's log in `<project>\scheduler\`, the tray's copy in `ProjectToolkit\tray\<key>\`, one folder per kit folder (967-B, 1265-B), the moves, stop and message files in the log folder. | `PoolConfigFile.LogDirOf`, `SchedulerConfigLoader` (`logDir`), `TraySelfCopy.Folder` |
| Beside the exe | **Read only**: `theme.default.json` and `themes\` (page themes), the tray icon. Nothing is written there. The page's firewall rule is bound to the exe's path (BUILD-3). | `PageThemeLoader`, `PoolThemeLoader`, `TrayIconLoader` |
| Tray | Started from anywhere, copies its own files to `%LOCALAPPDATA%\ProjectToolkit\tray\<key>\` on **every** start (`<key>` is `TraySelfCopy.KitKey` of the scheduler's folder: the end of `TrayExitSignal.NameFor`, 16 hex characters, so each kit folder has its own copy, 1265-B) and runs the copy with `-SchedulerDir:` pointing back, so the scheduler's folder holds no running tray image. | `Scheduler.Tray/Program.cs`, `TraySelfCopy.cs` |
| "Update and restart" (967-B) | `DeployLayout.ForSchedulerExe`: `out\windows` is the exe's grandparent and the kit root two above it; it runs `dotnet publish <kit>\code\src\Scheduler.Host` into `scheduler.next`, waits for workers' `lock`/`check-push` calls (150 s), swaps folders, starts the new build from the same path, swaps back if it does not start in 30 s. Without the project it fails: "there is no project to publish". Needs the .NET SDK. `bin\deploy-scheduler.ps1` is the same from the command line; `bin\build-and-deploy.ps1` pulls main first. | `DeployLayout.cs`, `SchedulerUpdater.cs`, `TrayApp.UpdateAndRestartAsync` |
| Stopping a scheduler | A named shutdown event per repository (per pool file for a pool) that any tray can set; Restart ends the scheduler and leaves the workers running for the next scheduler to adopt. | `Scheduler.Tray/RepoShutdown.cs`, TRAY-5, TRAY-8 |
| Runtimes | Framework-dependent, `net10.0` (Host) and `net10.0-windows` with WinForms (tray): users need the **.NET 10 Desktop Runtime** (x64), which includes the base runtime. No ASP.NET Core. | `Directory.Build.props`, `Scheduler.Tray.csproj` |
| The page on the LAN | `listen: lan` binds `0.0.0.0`; when that fails the page serves localhost only. No URL reservation is needed for this socket; Windows Defender asks about the inbound rule the first time. | `PageListening.cs` |
| The release | `publish-release.ps1` `git archive`s a main commit minus `$Exclude` (jobs, perf, developer-only bin scripts) into the `release` branch, `dotnet publish`es `code\src\Scheduler.Host` (which builds the tray too) into a temp stage, drops the pdbs, adds `LICENSE`, zips it, and `gh release create`s or `upload`s `ProjectToolkit-win-x64.zip`. Version from `code\src\Directory.Build.props`. | `bin/publish-release.ps1` `New-BinaryZip`, `Publish-GitHubRelease` |
| The guide | Clone the `release` branch, then `.\bin\build-and-deploy.ps1 -NoPull` (needs Git and the .NET 10 **SDK**). The README offers the zip. Nothing covers PowerShell's execution policy or SmartScreen. | `docs/guide/getting-started.md` sections 2 and 3.6, `README.md` "Install" |

What follows from this: the scheduler and `projecttoolkit.ps1` already run from any folder that has the release tree
with the binaries in `out\windows\scheduler\`, and they write nothing into it. Three things do not work from an
installed copy: template refresh (it needs a git clone), "Update and restart" (it needs the source and the SDK), and a
clean way for an installer to stop a running tray and scheduler. Section 2 fixes those three.

## 1. The installer tool

| Option | For | Against |
|---|---|---|
| **Inno Setup 6** (`ISCC.exe`) | One text script, free (its own licence, commercial use allowed), per-user installs without admin, Add/Remove Programs entry, uninstaller, tasks (checkboxes), PATH and Run-key entries, Pascal code for checks, silent switches for tests. In winget. Small output (LZMA2). | A second language (Pascal script) for the checks. |
| WiX Toolset (v5/v6, `dotnet tool install --global wix`) | MSI, installs with the .NET SDK we already have. | MSI authoring is verbose; per-user MSIs and custom actions (stopping the tray, checking the runtime) are much more work; a bootstrapper (Burn) for a runtime check is a second project. |
| MSIX | Clean install and removal. | Needs a signing certificate to install at all; the package folder is read-only and virtualised, file-system and registry writes are redirected, and the workers' child processes and `%LOCALAPPDATA%` state fit badly. |
| Self-extracting zip / script | No tool. | No uninstall, no Add/Remove Programs, no upgrade logic. |

**Recommendation: Inno Setup 6.** It is the shortest way to every requirement here and is what the plan expected.

- **INS-1** The installer is built with Inno Setup 6 (6.3 or later) from one script in the repository,
  `code\src\Installer\ProjectToolkit.iss`, compiled by `ISCC.exe`. The output is one file with a fixed name,
  **`ProjectToolkit-Setup.exe`**, so `releases/latest/download/ProjectToolkit-Setup.exe` always works. Everything the
  build passes in comes as ISCC defines: `/DAppVersion=<version>` (from `Directory.Build.props`) and
  `/DSourceDir=<the staged tree>`; the script names no machine path.
- **INS-2** How `publish-release.ps1` finds ISCC, first match wins: its `-Iscc <path>` parameter; `iscc` on PATH; the
  `InstallLocation` of an `Inno Setup 6_is1` uninstall key (HKLM, both registry views, then HKCU);
  `%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe`; `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`. It looks **before**
  anything is committed, tagged or pushed; not found, it stops with one line naming the install command below.
- **What the developer installs** (once, on the release machine): `winget install --id JRSoftware.InnoSetup -e`
  (check the id with `winget show JRSoftware.InnoSetup` first; it was not checked from here, there is no web access in
  this session). Nothing else is new: `gh`, Git and the .NET 10 SDK are already needed by `publish-release.ps1`.

## 2. What goes in it

- **INS-3** The installer holds the release tree exactly as the `release` branch has it for that version (the same
  `git archive` and `$Exclude` list as today), **less** `code\` and `test\` (the source; it stays in the release
  branch and on GitHub) and the two scripts that build from it, `bin\deploy-scheduler.ps1` and
  `bin\build-and-deploy.ps1`; **plus** the prebuilt scheduler and tray from `dotnet publish code\src\Scheduler.Host -c
  Release`, without pdbs, in `out\windows\scheduler\`, and `assets\templates\template-history.json` (INS-17).
  `LICENSE` is in the tree and is also the installer's licence page.
  *Why leave the source out:* the installed copy is for running, not building; without the source the installed copy
  is plainly an installed copy (INS-18), and the installer is a few MB smaller.
- **INS-4** The layout inside the install folder is the release tree's own, with the binaries at
  `out\windows\scheduler\`. Nothing in the code changes for that: `ToolkitLocator`'s search above the exe finds the
  install folder, `projecttoolkit.ps1` finds `assets\templates` beside its `bin\`, and the page's themes are beside the
  exe as today.

## 3. Where it installs

| Option | For | Against |
|---|---|---|
| **Per user, `%LOCALAPPDATA%\Programs\ProjectToolkit`** | No admin prompt; the user can replace files if something goes wrong; matches where every other piece of state already lives (`%LOCALAPPDATA%\ProjectToolkit`); the developer's tools (Claude Code, Copilot CLI, VS Code) install the same way. | One user only (a second Windows user installs again). |
| All users, `Program Files\ProjectToolkit` | One install for every user of the PC. | Admin for every install and upgrade; the folder is read-only for the user (the code is ready for that, INS-5, but a hand fix is harder). |
| A folder of the user's choice (`C:\Projects\ProjectToolkit`) | Where the guide puts the clone today. | Mixes an installed copy with source checkouts; easy to confuse with a clone. |

**Decided (Q1, developer, 2026-11-24): per user by default**, with the folder page shown so a user can pick another folder, and Inno's
"install for all users" choice allowed (`PrivilegesRequiredOverridesAllowed=dialog commandline`).

- **INS-5** `PrivilegesRequired=lowest`; default folder `{localappdata}\Programs\ProjectToolkit` (`{autopf}` resolves
  to it for a per-user install, to Program Files for all users). The program writes nothing into the install folder at
  run time (state stays in `%LOCALAPPDATA%`), so a read-only folder works; the section 3 test checks the folder is
  unchanged after a run.
- **INS-6** A fixed `AppId` (a GUID made once, written in the script, never changed), `AppName=ProjectToolkit`,
  `AppVersion` from INS-1, `AppPublisher=Trevor W. Walker`, `UninstallDisplayIcon` the tray's exe; Inno writes the
  Add/Remove Programs entry (HKCU for per-user).

## 4. What it adds to Windows

- **INS-7** A Start menu shortcut **Switchyard** to `{app}\out\windows\scheduler\scheduler-tray.exe -Pool`, working
  folder `{app}`, the tray's icon. No desktop icon.
- **INS-8** *Start at sign-in* is an installer task (a checkbox) that writes the value `Switchyard` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the same command as INS-7 (flag `uninsdeletevalue`).
  Decided (Q2, developer, 2026-11-24): offered, **unticked**.
- **INS-9** *`bin` on PATH* is an installer task that adds `{app}\bin` to the **user** PATH (`HKCU\Environment`,
  `ChangesEnvironment=yes`) and removes exactly that entry on uninstall. Decided (Q3, developer, 2026-11-24):
  offered, **unticked**. So the guide cannot assume PATH: it writes commands with the full path
  (`& "$env:LOCALAPPDATA\Programs\ProjectToolkit\bin\projecttoolkit.ps1" -Action new-project ...`) and says that
  ticking the box at install shortens them to `projecttoolkit.ps1 ...`.
- **INS-10** The installer adds no firewall rule and no URL reservation (both need admin, and the page works on
  localhost without them, `PageListening`). The exe's path does not change across upgrades (INS-13), so a rule the
  user allowed once stays valid (BUILD-3).
- **INS-11** The installer does not change PowerShell's execution policy. Files it writes carry no "downloaded from the
  internet" mark, so `RemoteSigned` is enough; the guide says how to set it
  (`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`) if `projecttoolkit.ps1` is blocked.

## 5. The .NET 10 runtime

| Option | For | Against |
|---|---|---|
| **Framework-dependent, check and offer the download** | Small installer; the runtime is patched by Microsoft Update; the build stays as it is. | One more install for the user. |
| Self-contained publish | Nothing else to install. | About 70 MB more; runtime fixes only arrive with our releases; two self-contained apps in one folder must be published together. |
| Bundle the runtime's installer | One download. | Needs admin to run it, which a per-user install avoids; a large installer. |

**Recommendation: framework-dependent, checked.**

- **INS-12** The installer checks for the **.NET 10 Desktop Runtime, x64** (`Microsoft.WindowsDesktop.App` 10.x):
  the folder named by `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\InstallLocation`, else
  `{commonpf64}\dotnet`, must have `shared\Microsoft.WindowsDesktop.App\10.*`. Missing, a page says so and offers a
  button that opens `https://dotnet.microsoft.com/download/dotnet/10.0` ("Desktop Runtime, Windows x64"); the
  installation still goes on, and the finish page does not offer to start Switchyard. It checks `git` on PATH the
  same way (a warning and the Git for Windows link; never a stop). The agent command lines are the guide's business.
- **INS-13 (first start)** The finish page offers "Start Switchyard" (ticked) only when the runtime is there **and**
  `%LOCALAPPDATA%\ProjectToolkit\pool.json` exists; with no pool file it says the next step is the guide's "create a
  project" (`projecttoolkit.ps1 -Action new-project`, which creates the pool file). A tray started with no pool file
  shows one message box naming that step instead of the scheduler's error (section 2).

## 6. Running from an installed folder (what section 2 changes)

- **INS-14** Each feature checks for what it needs, not for "installed": template refresh looks for `.git` in the kit
  (INS-17), "Update and restart" for `code\src\Scheduler.Host\Scheduler.Host.csproj` (INS-18). There is no marker file.
  A clone keeps working exactly as today; a pool or project `toolkit` setting that names a clone still wins over the
  exe search.
- **INS-15** Nothing new is written beside the exe or anywhere under the kit. New state, if any, goes under
  `%LOCALAPPDATA%\ProjectToolkit`.
- **INS-16** `projecttoolkit.ps1` needs no change to find the templates. Its help text (line 16) still says to
  `dotnet publish` and start the tray; in an installed kit (no `code\src`) it says "start Switchyard from the Start
  menu" instead.
- **INS-17 (template refresh without git)** When the kit folder has no `.git`, `TemplateRefresh` runs **no git
  process for the kit** (no fetch, no history). It reads each template from the kit's files on disk, and takes the past
  versions of each template from `assets\templates\template-history.json`, which `publish-release.ps1` writes into
  the release from the kit's git history: `{ "version": "<kit version>", "templates": { "<template path>": ["<git blob
  id>", ...] } }`, every blob id the template has had on main. Blob ids are git's (`git hash-object`), so the
  project side stays as today. It checks at start and whenever the manifest's `version` differs from the last one
  checked (that is, after an upgrade), in place of "the kit's `origin/main` moved". Edited files, Keep and Overwrite
  behave as today. A missing or unreadable manifest: one log line, nothing kept in step (as today with no `.git`).
- **INS-18 ("Update and restart" in an installed copy)** When there is no `Scheduler.Host` project above the running
  scheduler, the tray's menu item does not publish. Decided (Q4, developer, 2026-11-24): it reads
  **"Check for update..."** and opens the
  latest release's page in the browser; the user runs the new installer, which does the swap (INS-19). Not chosen:
  hiding the item, or having the tray download and run the new installer itself (that would be its own later plan: a
  network download in the product, and signing matters more then). `deploy-scheduler.ps1` and
  `build-and-deploy.ps1` are not in an installed copy (INS-3).

## 7. Upgrades

- **INS-19** A new installer finds the old one by `AppId` and installs into the same folder with the same task choices
  (`UsePreviousAppDir`, `UsePreviousTasks`). Before copying (`PrepareToInstall`) it runs the old copy's
  **`scheduler-tray.exe -StopForUpdate`** (new in section 2) when that file exists, and does not use Windows' Restart
  Manager (`CloseApplications=no`): Restart Manager would close the scheduler without the Restart semantics.
  `-StopForUpdate` ends the running tray and the pool's scheduler the way Restart ends a scheduler (the workers keep
  running and the next scheduler adopts them, TRAY-5, TRAY-8), waits for workers' `lock` and `check-push` calls from
  the exe to end (up to `SchedulerUpdater.CallsWindow`, 150 s, as 1021-B), prints one line, and exits 0 when nothing of
  the kit is running any more, 1 otherwise. On 1 the installer shows the line with Retry and Cancel. The stop's
  exit code and its line are also written to the setup log (`Log`, so a `/LOG` run shows it happened and what it
  said).
  **The kit is the one folder it runs from (1258-B).** It never stops a Switchyard running from another folder (a
  development checkout beside an installed copy, or a test install's scratch folder): schedulers and calls are
  counted by their exe's full path (`MainModule.FileName`), not by name; the tray's exit signal is a named event keyed
  by a hash of its `scheduler-next.exe` folder (`TrayExitSignal.NameFor`; every tray runs from the one copy of 967-B,
  so its own exe cannot tell kits apart); and the pool's shutdown event, which is named by the pool file every
  Switchyard shares (one pool per user, MP-H1), is set only when a scheduler runs from this folder and none from
  another. A pool of this kit's started without a tray while another folder's scheduler also runs is therefore not
  asked to end; the stop waits for it as for any process from the exe and says so (exit 1, Retry). A Switchyard
  from another folder is named in the line ("another Switchyard runs from <folder>; left alone") and is not a failure.
- **INS-20** The installer deletes `{app}\out\windows\scheduler\` and `{app}\assets\templates\` before copying
  (`[InstallDelete]`), so files a new version dropped do not linger. Nothing else under `{app}` is user data.
- **INS-21** After an upgrade, if a tray was running before (INS-19 found one) or start at sign-in is ticked, the
  installer starts the tray again (`-Pool`), whose new scheduler adopts the workers. An older installer run over a
  newer install asks before going on. The tray's copy in `%LOCALAPPDATA%\ProjectToolkit\tray\<key>\` needs nothing: the
  tray copies itself on every start (`TraySelfCopy`).

## 8. Uninstall

- **INS-22** Uninstall runs `-StopForUpdate` as INS-19, which stops only the Switchyard of the folder being
  removed and leaves one running from any other folder alone (workers still running finish on their own; their move
  requests wait in the moves folder for the next scheduler), then removes `{app}` whole, the Start menu shortcut, the
  Run value (INS-8), its PATH entry (INS-9) and, by running the installed `scheduler-tray.exe -RemoveTrayCopy`, the tray's program copy of this kit folder alone, `%LOCALAPPDATA%\ProjectToolkit\tray\<key>\` (1265-B; a copy of another kit folder is left, and so is any file in the old shared `tray\` folder; a non-zero exit is not an error for the uninstall). It
  **keeps** everything else under `%LOCALAPPDATA%\ProjectToolkit` (the pool file, logs, state) and every project's
  own `%LOCALAPPDATA%\<project>\` folder, and never touches a project repository. A reinstall finds the pool as it was.
  A tray never reaches another folder's Switchyard either (1264-B): the pool's shutdown event is named by the pool
  file alone, so every folder's scheduler holds the same one, and a tray attaches to a running pool or repository
  scheduler, and so can set that event or end that process, only while a `scheduler-next.exe` of its own kit folder
  (the folder of the scheduler it runs) is running; folders are compared ignoring case and a trailing slash. When
  the event exists and a `scheduler-next.exe` runs from another folder but none from its own, the tray shows one
  message box (`This pool is run by the Switchyard in <folder>.`, and to use that one's icon or exit it first),
  starts nothing, sets nothing, does not copy itself (967-B) and exits with 0; `-StopForUpdate` is unchanged. When
  the event exists but no `scheduler-next.exe` is visible from any folder (it is ending), the tray starts as it
  does once the event has gone; the scheduler it starts waits for the pool's mutex and never ends another.

## 9. Code signing

| Option | Cost | Effect |
|---|---|---|
| **Unsigned** | None | SmartScreen shows "Windows protected your PC" for the installer: *More info*, then *Run anyway*. Windows shows "Unknown publisher". |
| Azure Trusted Signing | About US$10 a month, identity check | Signed by the developer's name; SmartScreen still warns until the file builds reputation, less often. Availability for individuals depends on country. |
| OV certificate | A few hundred US$ a year, hardware token | As above. |
| EV certificate | More, hardware token | SmartScreen reputation from the start. |

**Decided (Q5, developer, 2026-11-24): unsigned for now**, with signing ready to switch on.

- **INS-23** The script has a `SignTool` hook behind `#ifdef Sign` (signing the setup and its uninstaller), off by
  default; signing later is a `publish-release.ps1` switch, not a new design. The release notes give the installer's
  SHA-256 so a user can check the download, and the guide shows the SmartScreen steps and says why it warns.

## 10. How publish-release.ps1 builds and attaches it

- **INS-24** `publish-release.ps1`:
  1. checks for `gh` (as today) and ISCC (INS-2) before anything else;
  2. after the `git archive` of the release commit (as today), stages the installer's tree from it (INS-3):
     `dotnet publish code\src\Scheduler.Host -c Release -o <stage>\out\windows\scheduler`, pdbs removed, the
     `code\`, `test\` and build scripts left out, `template-history.json` written from `$Repo`'s history at that
     commit (INS-17);
  3. runs `ISCC.exe /DAppVersion=<version> /DSourceDir=<stage> /O<temp> code\src\Installer\ProjectToolkit.iss`
     (ISCC's output to a file, the last lines shown on failure), then the section 3 test (`bin\test-installer.ps1`,
     developer-only, added to `$Exclude`); any failure stops it before the commit, tag and push, as the build failures
     do today;
  4. attaches `ProjectToolkit-Setup.exe` to the GitHub release in place of the zip (`gh release create` or
     `upload --clobber`, as today). The zip is no longer built; releases that have one keep it.
     `-ZipOnly` becomes `-InstallerOnly` (`-ZipOnly` still accepted, meaning the same);
  5. release notes: "Download ProjectToolkit-Setup.exe and run it", the runtime line (Desktop Runtime), the
     SmartScreen line, the SHA-256, and the `latest/download` link.

  The section 3 test installs silently into a scratch folder (`/VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /NOICONS
  /TASKS="" /DIR=<scratch>`) from a build compiled with a **test AppId** (`/DTest`, so it never upgrades or removes a
  real install), checks the files (both exes, no pdbs, `assets\templates`, `template-history.json`,
  `bin\projecttoolkit.ps1`, `LICENSE`, no `code\`), runs `projecttoolkit.ps1 -Action new-project -NoPool` into a
  scratch repository to prove the templates are found, uninstalls silently, and checks the folder is gone and
  `%LOCALAPPDATA%\ProjectToolkit\pool.json` is unchanged. Before installing anything it refuses to start (exit 1, one
  line) while a Switchyard (`scheduler-next.exe` or `scheduler-tray.exe`) runs from anywhere, unless
  `-AllowRunningSwitchyard` is given (1258-B): a guard on top of INS-19's own folder rule.

## Decisions by the developer

Asked on card 1242-A and answered by the developer on 2026-11-24; the rules above already follow them.

1. **Install location** (INS-5): per user in `%LOCALAPPDATA%\Programs\ProjectToolkit`, all-users allowed (as
   recommended). Not chosen: Program Files for everyone; a folder the user picks, default `C:\Projects\ProjectToolkit`.
2. **Start at sign-in** (INS-8): offered, unticked (as recommended).
3. **`bin` on PATH** (INS-9): offered, **unticked** (the recommendation was ticked). The guide writes full paths
   (card 4a).
4. **"Update and restart" in an installed copy** (INS-18): becomes "Check for update..." and opens the latest release
   page (as recommended).
5. **Signing** (INS-23): unsigned for now; the guide explains the SmartScreen warning (as recommended).

## Card list for plan sections 2-4

For the planner to card now that the questions are answered (worker types per `jobs/project.md`). Scopes are the files each card
changes; tests go with each code card in the matching `test/unit/` project.

**Section 2: make the kit run from an installed folder**

| # | Title | Scope | Worker |
|---|---|---|---|
| 2a | Template refresh in a kit without git (INS-17): no kit git process, templates from disk, past versions from `template-history.json`, the manifest `version` as the trigger | `code/src/Scheduler.Host/Templates/TemplateRefresh.cs` (+ a small `TemplateHistory.cs`), its tests | Sonnet |
| 2b | `bin/template-history.ps1`: write `template-history.json` for a commit from git history (every blob id of each template under `assets/templates`); called by 3b | `bin/template-history.ps1` (developer-only, add to `$Exclude`) | Spark |
| 2c | `scheduler-tray.exe -StopForUpdate` (INS-19): find and end the running tray and pool scheduler as Restart does, wait for `lock`/`check-push` calls, one line, exit 0/1 | `code/src/Scheduler.Tray/` (`TrayArguments.cs`, `Program.cs`, a new `StopForUpdate.cs`, `RepoShutdown.cs`), and the tray's own exit signal if it has none; tests | Opus |
| 2d | "Update and restart" in an installed copy (INS-18, per Q4) | `code/src/Scheduler.Tray/TrayApp.cs`, `DeployLayout.cs`; tests | Sonnet |
| 2e | Tray with no pool file (INS-13): one message box naming the guide's create-a-project step | `code/src/Scheduler.Tray/Program.cs` or `TrayApp.cs`; test | Spark |
| 2f | `projecttoolkit.ps1` help text in an installed kit (INS-16) | `bin/projecttoolkit.ps1` | Spark |

2a-2f change shipped code: each bumps the version (Rev).

**Section 3: the installer script and its build** (after 2b and 2c)

| # | Title | Scope | Worker |
|---|---|---|---|
| 3a | `code/src/Installer/ProjectToolkit.iss` per INS-1, 3-13, 19-23 (files, tasks, runtime and git checks, `PrepareToInstall` stop, restart after upgrade, uninstall, `#ifdef Sign`, `#ifdef Test` AppId) | `code/src/Installer/ProjectToolkit.iss` | Sonnet |
| 3b | `publish-release.ps1` builds and attaches the installer (INS-2, INS-24), `-InstallerOnly`, release notes, `$Exclude` additions | `bin/publish-release.ps1` | Sonnet |
| 3c | `bin/test-installer.ps1`: the silent install/uninstall test of INS-24, run by 3b | `bin/test-installer.ps1` | Sonnet |
| 3d | Review on a real machine: install, first start, upgrade over a running pool with workers, uninstall (developer's hands for SmartScreen and the runtime page) | none (review card) | Opus |

**Section 4: docs** (after 3b)

| # | Title | Scope | Worker |
|---|---|---|---|
| 4a | Getting started: Requirements (Desktop Runtime, no SDK needed), install with the installer, SmartScreen and execution-policy notes, first start from the Start menu, commands with the kit's full `bin` path (PATH is unticked by default, INS-9) and a note that ticking it shortens them; building from a clone stays as the second path | `docs/guide/getting-started.md` | Spark |
| 4b | README "Install" and the guide's tray page (Start menu, sign-in, "Check for update...", uninstall keeps data) | `README.md`, `docs/guide/switchyard.md` | Haiku |
