# Sidebar scroll accessibility correction

Date: 2026-08-20. Status: `IMPLEMENTED / VISUAL ACCEPTANCE OPEN`.

## Proven problem and correction

- Fresh-app visual QA at the default approximately 1280×720 window proved that
  the fixed left sidebar was taller than its viewport. The MEXC API card and its
  Keychain profiles were below the visible area with no way to reach them.
- The existing sidebar content is now wrapped in Avalonia's vertical
  `ScrollViewer`; horizontal scrolling is disabled.
- Existing width, content order, hierarchy, credential controls and all trading,
  security and Keychain logic are unchanged.
- The scrollable content includes both `READ-ONLY` and `ORDER TEST` profiles,
  their masked fields, save controls and conditional two-step revoke controls.

## Verification boundary

- Release solution/XAML compilation: PASS, 0 warnings, 0 errors.
- One final self-contained `osx-arm64` publish updated only the existing app.
- Publish/app `Trdng.Desktop.dll` SHA-256 match:
  `ec46cc3b49e0b10592069949a5126421140cf54cdfc656c618e9ea4874c13be3`.
- Signed app executable SHA-256:
  `2f240deadfb6efa5635c2b92d76844557b1c00c5704c023a3cf600cad2489edb`.
- Strict deep codesign verification and `git diff --check`: PASS.
- No real credential, network/authenticated request, order-test or trading action
  is part of this correction.
- GUI is not run by the implementation task. One fresh-app visual acceptance by
  the independent Assistant remains required.
