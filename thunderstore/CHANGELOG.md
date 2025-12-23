## Changelog

### 0.1.5
- **Client/Guest tip**: Installing **[TooManyItems](https://thunderstore.io/c/lethal-company/p/mattymatty/TooManyItems/)** may help with a vanilla issue where some items fail to sort / snap back.
- **New short commands**:
  - `/sr` → `/sort reset`
  - `/sp` → `/sort positions`
  - `/sbl` → `/sort bindings`
  - `/sk ...` → `/sort skip ...` (e.g. `/sk list`, `/sk add`, `/sk remove`)
- **Input aliases (built-in)**:
  - `double_barrel` → `shotgun`
  - `shotgun_shell` → `ammo`
- **Full sort ordering**: two-handed item types are placed first (fixed behavior; no setting).
- **Full sort placement**:
  - `sortOriginY` is applied again as an offset above detected ground.
  - Specific types are placed slightly lower: `toilet_paper`, `chemical_jug`, `cash_register`, `fancy_lamp`, `large_axle`, `v_type_engine`.
- **Config**:
  - Added `General.configVersion` (defaults to `0.1.5`).
  - Migration: if `configVersion` is missing/older and `Sorter.sortOriginY == 0.5`, it is auto-changed to `0.1`.
  - Migration: adds `shotgun`, `ammo` to `Sorter.skippedItems` if missing.

