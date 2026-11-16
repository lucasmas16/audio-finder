# Audio Finder

**English** | [Português (Brasil)](README.pt-BR.md)

Windows tray app that switches the default audio output with global shortcuts, with up to eight positions and a history of Bluetooth devices.

## Download

[![Download the Windows installer](https://img.shields.io/badge/Download_installer-Windows_10%2F11_64--bit-2ea44f?style=for-the-badge)](https://github.com/lucasmas16/audio-finder/releases/latest/download/AudioFinder-Setup.exe)
&nbsp;
[![Download the portable version](https://img.shields.io/badge/Portable_version-.zip-555555?style=for-the-badge)](https://github.com/lucasmas16/audio-finder/releases/latest/download/AudioFinder-Portable.zip)

[![Latest version](https://img.shields.io/github/v/release/lucasmas16/audio-finder?label=latest%20version)](https://github.com/lucasmas16/audio-finder/releases/latest)

The buttons always download the latest version.

1. Click **Download installer** and open `AudioFinder-Setup.exe`.
2. If Windows shows "Windows protected your PC", click **More info › Run anyway**. The warning appears because the installer is not digitally signed yet ([how to verify where it came from](#install)).
3. Click **Install**. No administrator rights needed. Audio Finder opens on the icon next to the clock and stays in the Start menu.

| File | Who it is for |
| --- | --- |
| [`AudioFinder-Setup.exe`](https://github.com/lucasmas16/audio-finder/releases/latest/download/AudioFinder-Setup.exe) | Most people: installs, creates the shortcut and can be uninstalled from **Settings › Apps** |
| [`AudioFinder-Portable.zip`](https://github.com/lucasmas16/audio-finder/releases/latest/download/AudioFinder-Portable.zip) | If you prefer not to install: extract it and run `AudioFinder.exe` |
| [`SHA256SUMS.txt`](https://github.com/lucasmas16/audio-finder/releases/latest/download/SHA256SUMS.txt) | Check the integrity of the files |

Requires 64-bit Windows 10 (1903 or later) or Windows 11. Older versions and release notes are under [Releases](https://github.com/lucasmas16/audio-finder/releases).

## Features

- Set up audio outputs and global shortcuts.
- Settings, Bluetooth history and recovery from a backup copy are saved.
- English and Portuguese interface: follows the Windows display language and can be switched with the **PT | EN** selector in the main window. The installer follows the Windows language.
- Light/dark theme, on-screen notices and optional start with Windows.
- Horizontal switch bar with the shortcut positions, an image per device and an enlarged highlight for the selected output.
- When you activate a disconnected Bluetooth output, it asks the existing audio driver to connect, shows progress with the device's image and waits up to 15 seconds for the output to become active. Failures use a picture of the computer and the device.
- Nine image models (in-ear earbuds, open-fit earbuds, studio speakers, premium headphones, gaming headset, wireless over-ear headphones, monitor/TV, sports earbuds and bone-conduction headset), plus the default icon and your own PNG in the editor.

## Technologies

C#, Windows Forms, .NET Framework 4.8 and native Windows APIs.

## Install

Use the [Download](#download) buttons. It installs for the current user only, in `%LOCALAPPDATA%\Programs\AudioFinder`, without administrator rights, with a Start menu shortcut and uninstall from **Settings › Apps**. Updating keeps settings and shortcuts. A portable `.zip` is also available.

The executables are not digitally signed yet, so Windows SmartScreen may warn about an "Unknown publisher". The build is already prepared for signing ([code signing](docs/ASSINATURA.md), in Portuguese). To verify that a file came from this repository's pipeline:

```powershell
gh attestation verify .\AudioFinder-Setup.exe --repo lucasmas16/audio-finder
Get-FileHash .\AudioFinder-Setup.exe -Algorithm SHA256   # compare with SHA256SUMS.txt
```

Installer options: `/silent`, `/desktop`, `/nolaunch`, `/dir=<folder>`. Silent uninstall: `Uninstall.exe /uninstall /silent` (add `/removedata` to also delete `%LOCALAPPDATA%\AudioFinder`). Errors in silent mode go to `%TEMP%\AudioFinderSetup.log`.

## Run from source

Requires Windows 10/11 x64 and .NET Framework 4.8. The script uses the compiler installed with the Framework; no NuGet needed.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\dist\AudioFinder.exe
```

The project can also be opened from `AudioFinder.csproj` in Visual Studio with the 4.8 targeting pack.

## Build a release

`powershell -ExecutionPolicy Bypass -File .\build.ps1 -Release` creates the installer, the portable `.zip` and `SHA256SUMS.txt` in `release/`, using the version in the `VERSION` file. The package contains a fixed list of files only: executable, config, license and icons. `.\tests\installer-smoke.ps1` installs into a temporary folder, reinstalls, checks the safety refusals and uninstalls.

To publish, update `VERSION`, commit and push the matching tag (for example `git tag v1.0.0` and `git push origin v1.0.0`). The [release](.github/workflows/release.yml) pipeline builds, runs the privacy check and the deterministic tests, tests the installer, creates the provenance attestation and opens a **draft** release for review. Details in [publishing](docs/PUBLICACAO.md) (in Portuguese). `-SignScript tools\sign.ps1` signs the app and the installer during the build, and `-RequireSigned` fails if anything comes out unsigned.

## Test

`powershell -ExecutionPolicy Bypass -File .\build.ps1 -RunTests`. The suite talks to the audio APIs and may temporarily change the default output; run it outside calls or recordings. A plain build does not run these tests. Use `-RunTests -SafeTests` to check the interface, persistence and shortcuts without changing the audio output. Native shortcuts need access to the interactive Windows session.

Before preparing a commit: `node tools/check-privacy.cjs` (Node.js 24).

Identity tests without changing audio or registering shortcuts: `powershell -ExecutionPolicy Bypass -File .\tests\device-identity.ps1`.

Deterministic connection, identity, cancellation and timeout tests, without connecting hardware: `powershell -ExecutionPolicy Bypass -File .\tests\bluetooth-connection.ps1`. The `-Probe` option adds a read of the driver capability (`BASICSUPPORT`) without sending the reconnect request. The interface suite injects a controlled failure for fake Bluetooth devices. The integration between controller and notices is tested with a fake backend for progress, another slot, late completion, cancellation, timeout and disabled notices: `powershell -ExecutionPolicy Bypass -File .\tests\connection-ui-flow.ps1`.

Each entry stores the identity of the physical interface and the Windows container. If an endpoint is recreated, the association and the shortcut are only updated when the identity is proven, the audio profile matches and there is a single target. A matching name alone does not identify hardware; old entries without identity or ambiguous targets are kept as they are. Identity is collected when the original endpoint is found again or when an available output is set up.

## Project layout

- `src/Program.cs`: startup and lifecycle.
- `src/Audio.cs`: enumeration, Core Audio notifications and output switching.
- `src/Hotkeys.cs`: native shortcuts.
- `src/Models.cs`: preferences and persistence.
- `src/Views.cs` and `src/Appearance.cs`: interface and theme.
- `src/Localization.cs`: English and Portuguese texts and the language choice.
- `installer/`: per-user installer with no external dependencies.
- `tests/`: test suites; `build.ps1`: build and packages; `.github/workflows/`: CI and releases.

More details, in Portuguese: [architecture](docs/ARQUITETURA.md), [initial decision](docs/decisions/0001-organizacao.md), [privacy](docs/PRIVACIDADE.md) and [contributing](CONTRIBUTING.md).

## Limitations

For local diagnostics, start with `--diagnostics` (close the running instance first). The file `%LOCALAPPDATA%/AudioFinder/connection-diagnostic.log` records connection stages, shortcut position, window state and timestamps, capped at about 256 KiB. It does not record device names or IDs and sends nothing. Without the option, this log stays off. The `connection-ui-flow.ps1` suite also checks the result pixels on the desktop and the real tray loop with concurrent events; it needs an interactive session.

The switch affects the default playback outputs; apps pinned to a specific device may ignore it. The internal `IPolicyConfig` interface has no stable public contract. Hardware, Bluetooth and physical key checks remain manual.

Connecting uses the public [KSPROPERTY_ONESHOT_RECONNECT](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect) property through the Core Audio topology and `IKsControl`. The driver must advertise support; accepting the request does not prove a connection, so the app only switches the output after confirming an active endpoint. It does not install services or restart drivers. Outputs without support show a failure and suggest connecting from Windows settings. Cancelling or choosing another slot stops the wait and prevents a later switch; a request already handed to the driver may still connect the device. The app does not send a disconnect to undo that request.

## Next steps

- Separate logic tests from tests that touch hardware.
- Consider newer .NET versions only after measuring compatibility and resource use.

## Code signing policy

Releases are **not code signed yet**. Until then, verify their origin with the pipeline attestation (`gh attestation verify`) and the `SHA256SUMS.txt` of each release, as described in [Install](#install).

- Committers and reviewers: [@lucasmas16](https://github.com/lucasmas16)
- Approvers: [@lucasmas16](https://github.com/lucasmas16)

When signing is enabled, only binaries built from this repository will be signed, and each signature will require manual approval by the maintainer. Details in [code signing](docs/ASSINATURA.md) (in Portuguese).

Privacy policy: this program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License

[MIT](LICENSE). The icons in `assets/devices` are generic illustrations of device categories, with no link to any manufacturer.

Custom images are copied to `%LOCALAPPDATA%/AudioFinder/device-images`; moving the original PNG does not change the saved image. A missing or invalid file falls back to the chosen model and, if needed, to the default drawing. To distribute the app, keep `assets/devices` next to the executable; both build flows copy these assets.
