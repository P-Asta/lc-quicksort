## QuickSort (pasta.quicksort)

**English(Support)** | [**한국어**](https://github.com/p-asta/lc-quicksort/blob/main/docs/README-kr.md) <br/>
Ship and cruiser item sorting, quick move commands, and named profiles for Lethal Company.

## Client/Guest note (IMPORTANT)
If you are a **client (guest)**, installing **[TooManyItems](https://thunderstore.io/c/lethal-company/p/mattymatty/TooManyItems/)** can help fix a issue where **some items fail to sort / snap back** in the ship.

## Commands
- **Tip**: If a command shows **`[itemName]`**, the name is optional — if you omit it, the command will use the **item you are currently holding** (if any).
- **`/sort`**: Full sort of eligible items in the ship and cruiser, bringing cruiser cargo into the ship layout.
  - Uses `skippedItems` as the skip list for full sort.
  - An item you are holding stays in your hand.
- **`/sort -a`**: Full sort, but **IGNORE `skippedItems`** (sort absolutely everything that is eligible).
- **`/sort -b`**: Full sort with “saved position priority”.
  - If an item type has a saved `/sort set` position, it will **NOT** be skipped even if it matches `skippedItems`.
  - It also brings matching cruiser items to their saved **ship** position. A `/css` cruiser position does not determine their ship destination or override `skippedItems`.
  - Otherwise (no saved position), `skippedItems` still applies.
  - **Note**: `-a` and `-b` cannot be combined (and `/sort -ab` / `/sort -ba` are rejected).
- **`/sort <itemName>`**: Move that item type from the ship or cruiser to your current ship position (e.g. `/sort cash_register`, `/sort weed killer` or `/sort wee`).
  - This explicit move **ignores skip lists**, so it works even if the type is in `skippedItems`.
- **`/sort <number>`**: Move the item type bound to that number (e.g. `/sort 1`).
- **`/pile [itemName]`**: Like `/sort <itemName>` (pull a specific type to you), but **if omitted it uses your held item type and also moves the held item**.

### Skip list (skippedItems)
Edit `skippedItems` in-game:
- **`/sort skip list`**: Show current `skippedItems` tokens
- **`/sort skip add [itemName|alias|id]`**: Add a token
  - If omitted, uses your **currently held item**.
  - Also accepts alias or shortcut id.
- **`/sort skip remove [itemName|alias|id]`**: Remove a token
  - If omitted, uses your **currently held item**.
  - Also accepts alias or shortcut id.

### Bindings (shortcut + alias)
Bind the item you are currently holding:
- **`/sort bind <name|id>`**
  - **`/sort bind 1`** → bind held item to shortcut id 1
  - **`/sort bind meds`** → bind held item to alias `meds`
- **`/sort bind reset <name|id>`**
  - **`/sort bind reset 1`** → remove shortcut id 1 binding
  - **`/sort bind reset meds`** → remove alias `meds` binding
- **`/sb <name|id>`**: same as `/sort bind ...`
- **`/sb reset <name|id>`**: same as `/sort bind reset ...`

List bindings:
- **`/sort bindings`** (also accepts `/sort binds`, `/sort shortcuts`, `/sort aliases`)

Use bindings:
- **`/sort 1`** (number binding)
- **`/sort meds`** (alias binding)

### Saved positions
- **`/sort set [itemName]`**: Save this type's sort position to your current ship position, then move matching ship and cruiser items there (**partial match supported**).
- **`/ss [itemName]`**: same as `/sort set ...` (**partial match supported**).
- Ship positions saved with `/ss` are separate from cruiser positions saved with `/css`, even for the same item type. `/sort -b` uses the ship position; `/cs` uses the cruiser position and maximum.
- **`/sort reset [itemName]`**: Delete saved sort position.
- **`/sr [itemName]`**: same as `/sort reset ...`
- **`/sort positions`**: List saved sort positions.
- **`/sp`**: same as `/sort positions`
- **`/sbl`**: same as `/sort bindings`
- **`/sk ...`**: same as `/sort skip ...` (e.g. `/sk list`, `/sk add ...`, `/sk remove ...`)

### Cruiser sorting

- **`/css <zone> [itemName] [max]`**: While on or near the cruiser, save a shelf zone for an item type and immediately place matching items there, up to the maximum. Omit the item name to use the held item, or `max` to use **10** (allowed range: 1–10000). For example, `/css A2 shotgun` places up to 10 shotguns on shelf zone A2; `/css A2 shotgun 2` places up to two.
- **`/css [itemName] [max]`**: While standing on the cruiser, save your current position instead of a shelf zone and immediately place matching items there. A matching item in your hand is also placed if there is room. The item name defaults to the held item; `max` defaults to **10**.
- **Alternative shelf forms**: `/css shelf A2 shotgun 2` and CruiserLoader-style `/css shotgun 2 A2` both save the same shelf rule as `/css A2 shotgun 2`.
- **`/css zones`**: List the available shelf zones. A, B, C, E, F, and G each have three shelf levels (`A1`–`A3`, etc.); `D1`–`D3` are the center spots. `D2` is the radar booster spot and holds one item. See [CruiserLoader's zone layout image](https://github.com/veber01/LC-CruiserLoader/blob/main/ZoneLayout.png) to choose a zone.
- **`/css list`**: Show saved cruiser positions and maximum counts.
- **`/css reset [itemName]`**: Remove a saved cruiser position. Omit the name to use the held item.
- **`/csort`**: Arrange configured items already in the nearest cruiser, including saved shelf zones, and load matching items from the ship, up to each type's saved maximum. You can run this command without standing on the cruiser. Items beyond a saved maximum stay where they are.
- **`/cs`**: Short form of `/csort`.

`/css` saves the selected cruiser position with **0.3 added to its cruiser-local Y coordinate**. Existing manually saved cruiser positions retain their old Y until you run `/css` again at that position. When arranging multiple items of the same type, `/css` and `/cs` keep their X/Z position fixed and increase only Y by the `sameTypeStackStepY` setting per item (default **0**, exact overlap). Shelf zones and maximum counts are saved with QuickSort's cruiser positions and included in QuickSort profiles. `/cs` uses these rules. A shelf zone holds at most **20 items total** across all types assigned to it; `D2` holds **one**. The `[max]` setting accepts values up to 10000, but sorting stops at the zone's capacity and reports when it is reached. Shelf placement moves items directly to their targets without a fall animation, reducing the chance of one item pushing another during sorting.

QuickSort keeps placed shelf items steady relative to the cruiser while they remain on the shelf, then releases them on pickup or removal. In multiplayer, full physics anchoring requires QuickSort on the item's authority (usually the host); direct placement still works when some players do not have QuickSort.

The cruiser shelf placement feature was developed with reference to [LC-CruiserLoader by veber01](https://github.com/veber01/LC-CruiserLoader).

### Profiles

Profiles save the ship sort settings (including `skippedItems`), ship positions, cruiser positions, and cruiser maximum counts. When you switch with `/pu`, changes to the current personal profile are saved automatically before the next one is applied. The built-in `default` profile is your personal baseline. `/ps [name]` creates or updates a snapshot under that name without rewriting the previous profile.

When a host profile is available in a lobby, it appears as a temporary `host` profile. **Sync Host Profile** is enabled by default and automatically applies it. Turn that setting off to keep your personal profile selected; `/pu host` still applies the host profile manually. Leaving the lobby restores your personal selection and live settings.

- **`/profile save [name]`**: Save the current settings and positions under a name; omit the name to save `default`. Saving the same name updates it.
- **`/profile use [name]`**: Apply a saved profile without starting a sort; omit the name to use `default`. Use `/pu host` for the current lobby host's profile when available.
- **`/profile sort [name]`**: Optionally apply a profile, then sort the ship.
- **`/profile csort [name]`**: Optionally apply a profile, then sort the cruiser.
- **`/profile list`**: List saved profiles and mark the active one.
- **`/profile delete <name>`**: Delete a saved profile.
- **Short forms**: `/ps [name]` = `/profile save [name]`, `/pu [name]` = `/profile use [name]`, `/pl` = `/profile list`, `/pd <name>` = `/profile delete <name>`.

## Config / files
All files are created under `BepInEx/config`.
- **Bindings**: `pasta.quicksort.sort.bindings.json`
- **Saved positions**: `pasta.quicksort.sort.positions.json`
- **Cruiser positions and maximum counts**: `pasta.quicksort.cruiser.positions.json`
- **Profiles**: `pasta.quicksort.profiles.json`
- **Temporary host profile recovery**: `pasta.quicksort.host-recovery.json` is created while a host profile is applied, then removed after personal settings are restored. It can restore those settings after an interrupted session.
- **Host profile sync**: BepInEx **Sync Host Profile** setting (enabled by default). Turn it off to prevent automatic use of the temporary host profile.

## Notes
- **Item name normalization**: spaces/hyphens are normalized to underscores for matching (e.g. `kitchen knife` → `kitchen_knife`).
- **Korean patch compatibility**: some Korean item name inputs are recognized as aliases (e.g. `머그잔`→`coffee_mug`, `쿠키 틀`→`cookie_mold_pan`, `식칼`→`kitchen_knife`, `산탄총`→`shotgun`).
- **Built-in input aliases**:
  - `double_barrel` → `shotgun`
  - `shotgun_shell` → `ammo`
- **Special Y offset (lower placement)**:
  - Full sort (`/sort`) will place these types slightly lower: `toilet_paper`, `chemical_jug`, `cash_register`, `fancy_lamp`, `large_axle`, `v_type_engine`
- **Explicit move ignores skip**: `/sort <itemName>` will still work even if that type is in `skippedItems` (fixes kitchen knife not moving).
- **Legacy config fix**:
  - If `skippedItems` contains `rader_booster` (old typo), it is auto-rewritten to `radar_booster`.
  - If a token accidentally has leading/trailing `_` (e.g. `_kitchen_knife`), it is normalized.
- **Config migration (0.1.5)**:
  - If `configVersion` is missing / older than `0.1.5`, and `sortOriginY` is `0.5`, it will be auto-changed to `0.1`.
- **Skip list choice**: Manually added `shotgun` and `ammo` tokens are preserved when the config is migrated.

## SS
![alt text](https://raw.githubusercontent.com/P-Asta/lc-QuickSort/refs/heads/main/assets/image.png)
