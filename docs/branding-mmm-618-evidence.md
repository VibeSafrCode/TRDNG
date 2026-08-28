# MacMakeMoney_618 branding evidence

Date: 2026-08-24.

## Product naming

- Full user-facing name: `MacMakeMoney_618`.
- Short user-facing name: `MMM_618`.
- Repository, executable, Keychain service and internal namespaces remain `TRDNG`
  for compatibility; the rename does not move credentials or user data.

## Icon

- Source PNG: `packaging/macos/MMM_618-icon.png`.
- macOS bundle resource: `packaging/macos/MMM_618.icns`.
- Visual: dominant gold `61.8`, an emerald `MMM` top petal, subtle market
  graphics and one centered right-edge bite notch.
- Built-in image generation was used for the raster source. Final prompt required
  exact `MMM` / `61.8` text, strong small-size readability and an original
  fruit-like silhouette rather than the Apple logo.

## Compatibility boundary

The bundle identifier remains `com.trdng.terminal`, the executable remains
`Trdng.Desktop`, and the Keychain service remains
`com.trdng.desktop.credentials.v1`. Changing those identifiers in this visual
rename would needlessly break existing macOS trust and credential continuity.
