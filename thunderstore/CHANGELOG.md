## Changelog

## 0.1.22
- Fixed cruiser shelf items tilting after sorting by applying their item-specific resting rotation relative to the vehicle.
- Kept `/ss` ship positions and `/css` cruiser positions independent for the same item type. `/sort` now collects eligible cruiser cargo into the ship; `/sort -b` returns types with a saved `/ss` position there even when skipped; `/cs` continues to use `/css` cruiser rules and maximum counts.
- `/css` now saves the cruiser-local Y position 0.3 higher. Previously saved manual positions keep their Y until re-saved. Cruiser sorting stacks items of the same type at a fixed X/Z, raising Y by `sameTypeStackStepY` per item (default 0), while retaining the shared shelf-zone capacity.

## 0.1.21
- Added numbered cruiser shelf zones (`A1`–`C3`, `D1`–`D3`, `E1`–`G3`) with `/css <zone> [itemName] [max]` and `/css zones`. `/css shelf <zone> ...` and CruiserLoader-style `/css <itemName> <max> <zone>` are supported as aliases. Shelf rules are saved and reused by `/csort` and `/cs`.
- Added direct shelf placement to prevent fall animations from pushing other cargo during sorting.
- Added cruiser-relative shelf anchoring while items remain on a shelf. Anchoring releases on pickup or removal; full multiplayer physics anchoring requires QuickSort on the item authority/host.
- Shelf zones support up to 20 items per zone (five slots × four layers), except `D2`, which supports one. Sorting warns when a configured maximum exceeds the zone's physical capacity.
- Added built-in `default`, temporary lobby `host`, and named `/profile` snapshots for ship and cruiser sorting settings. Optional **Sync Host Profile** automatically uses the host profile and restores personal settings when leaving the lobby.
- Added temporary `pasta.quicksort.host-recovery.json` recovery data to restore personal settings after an interrupted host-profile session.
- Added `/ps`, `/pu`, `/pl`, `/pd`, and `/cs` shortcuts for profile management and cruiser sorting.
- Added `/csort` to arrange cruiser cargo and load matching items from the ship while keeping cruiser cargo excluded from normal ship sorting.
- Fixed localized item skip matching and preserved user-selected shotgun/ammo skips during config upgrades.
- Documented the LC-CruiserLoader reference in the English and Korean READMEs.

## 0.1.16
- **Fix**
  - `shotgun` and `ammo` are no longer forced back into `skippedItems` after users remove them.
- **Compatibility**
  - Updated ChatCommandAPI support for `baer1-ChatCommandAPI-1.1.2`.

## 0.1.15
- **Change**
  - Modified to sort all items on the map in certain situations.


## 0.1.13
- **Fix**
  - chanload destory bug

## 0.1.11 / 0.1.12
- **Fix**
  - v80 :D

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

