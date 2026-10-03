## Changelog

### 0.1.22
- Fixed cruiser shelf items tilting after sorting by applying their item-specific resting rotation relative to the vehicle.
- Kept `/ss` ship positions and `/css` cruiser positions independent for the same item type. `/sort` now collects eligible cruiser cargo into the ship; `/sort -b` returns types with a saved `/ss` position there even when skipped; `/cs` continues to use `/css` cruiser rules and maximum counts.
- `/css` now saves the cruiser-local Y position 0.3 higher. Previously saved manual positions keep their Y until re-saved. Cruiser sorting stacks items of the same type at a fixed X/Z, raising Y by `sameTypeStackStepY` per item (default 0), while retaining the shared shelf-zone capacity.

### 0.1.21
- Added numbered cruiser shelf zones (`A1`–`C3`, `D1`–`D3`, `E1`–`G3`) with `/css <zone> [itemName] [max]` and `/css zones`. `/css shelf <zone> ...` and CruiserLoader-style `/css <itemName> <max> <zone>` are aliases. Saving a shelf rule immediately places matching items; QuickSort profiles save the rule, and `/csort` and `/cs` reuse it.
- Shelf zone placement now uses direct placement to avoid fall animation pushing other cargo during sorting.
- Added shelf anchoring relative to the cruiser while items remain on a shelf; anchoring releases on pickup or removal. Full multiplayer physics anchoring requires QuickSort on item authority/host.
- Shelf zones are capped at 20 items total per zone (five slots on four layers); `D2` is capped at one. Sorting reports when a saved maximum exceeds physical zone capacity.
- Added a built-in `default` baseline, a temporary lobby `host` profile, and optional automatic host profile use through the BepInEx **Sync Host Profile** setting (enabled by default). `/pu host` remains available manually, and personal settings return on lobby exit.
- Added temporary `pasta.quicksort.host-recovery.json` to restore personal settings after an interrupted host-profile session.
- Documented the LC-CruiserLoader reference in the English and Korean READMEs.

### 0.1.20
- Fixed `/csort` and `/cs` reporting "QuickSort is not ready yet" by using the active coroutine host instead of depending on `Plugin.Instance`.
- `/css` now immediately places the configured item type on the cruiser up to its saved maximum, including a matching held item when there is room.

### 0.1.19
- Hardened bare `/csort` and `/cs` so empty or invisible argument tokens do not trigger a usage error.
- Normalized empty arguments for all commands, including `/pl` and `/profile`.

### 0.1.18
- Added `/ps`, `/pu`, `/pl`, `/pd`, and `/cs` shortcuts for profile management and cruiser sorting.

### 0.1.17
- Fixed localized item skip matching and preserved user-selected shotgun/ammo skips during config upgrades.
- Kept cruiser cargo out of ship sorting.
- Added `/css` cruiser positions with maximum item counts and `/csort` to arrange cruiser cargo and load from the ship.
- Added named `/profile` snapshots for ship and cruiser sort settings.

### 0.1.16
- **Fix**:
  - `shotgun` and `ammo` are no longer forced back into `skippedItems` after users remove them.
- **Compatibility**:
  - Updated ChatCommandAPI support for `baer1-ChatCommandAPI-1.1.2`.

### 0.1.10
- **Fix**:
  - add some kr names
  - wight 0lb bug

### 0.1.9
- **Fix**:
  - add some kr names

### 0.1.8
- **Fix**:
  - `/sort`: now drops your held item first, then runs full sort.
- **Behavior**:
  - Commands now require you to be **inside the ship** (otherwise an error is shown): `/sort` (and `/ss`, `/sr`, `/sp`, `/sk`, `/sb`, `/sbl`) and `/pile`.

### 0.1.7
- **Config**:
  - Bumped `General.configVersion` to `0.1.7`.
  - Migration: if `Sorter.skippedItems` is accidentally only `shotgun, ammo`, it is reset back to the full default list.
  - Fresh install fix: when the config file does not exist yet, migrations no longer create/overwrite `Sorter.skippedItems`.

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

