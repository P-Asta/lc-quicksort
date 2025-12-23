## QuickSort (pasta.quicksort)

**English(Support)** | [**한국어**](https://github.com/p-asta/lc-quicksort/blob/main/docs/README-kr.md) <br/>
Ship item sorting + quick move commands for Lethal Company.

## Client/Guest note (IMPORTANT)
If you are a **client (guest)**, installing **[TooManyItems](https://thunderstore.io/c/lethal-company/p/mattymatty/TooManyItems/)** can help fix a issue where **some items fail to sort / snap back** in the ship.

## Commands
- **Tip**: If a command shows **`[itemName]`**, the name is optional — if you omit it, the command will use the **item you are currently holding** (if any).
- **`/sort`**: Full sort (sort everything in the ship).
  - Uses `skippedItems` as the skip list for full sort.
- **`/sort -a`**: Full sort, but **IGNORE `skippedItems`** (sort absolutely everything that is eligible).
- **`/sort -b`**: Full sort with “saved position priority”.
  - If an item type has a saved `/sort set` position, it will **NOT** be skipped even if it matches `skippedItems`.
  - Otherwise (no saved position), `skippedItems` still applies.
  - **Note**: `-a` and `-b` cannot be combined (and `/sort -ab` / `/sort -ba` are rejected).
- **`/sort <itemName>`**: Move that item type to your current position (e.g. `/sort cash_register`, `/sort weed killer` or `/sort wee`).
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
- **`/sort set [itemName]`**: Save this type's sort position to your current position (**partial match supported**).
- **`/ss [itemName]`**: same as `/sort set ...` (**partial match supported**).
- **`/sort reset [itemName]`**: Delete saved sort position.
- **`/sr [itemName]`**: same as `/sort reset ...`
- **`/sort positions`**: List saved sort positions.
- **`/sp`**: same as `/sort positions`
- **`/sbl`**: same as `/sort bindings`
- **`/sk ...`**: same as `/sort skip ...` (e.g. `/sk list`, `/sk add ...`, `/sk remove ...`)

## Config / files
All files are created under `BepInEx/config`.
- **Bindings**: `pasta.quicksort.sort.bindings.json`
- **Saved positions**: `pasta.quicksort.sort.positions.json`

## Notes
- **Item name normalization**: spaces/hyphens are normalized to underscores for matching (e.g. `kitchen knife` → `kitchen_knife`).
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
  - Also adds `shotgun`, `ammo` into `skippedItems` (if missing).

## SS
![alt text](https://raw.githubusercontent.com/P-Asta/lc-QuickSort/refs/heads/main/assets/image.png)