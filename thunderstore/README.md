## QuickSort (pasta.quicksort)
Ship item sorting + quick move commands for Lethal Company.

## Commands
- **`/sort`**: Sort everything in the ship (respects `skippedItems` for full-sort skip).
- **`/sort <itemName>`**: Move that item type to your current position (e.g. `/sort cash_register`).
- **`/sort <number>`**: Move the item type bound to that number (e.g. `/sort 1`).

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
- **`/sort set [itemName]`**: Save this type's sort position to your current position (name optional if holding).
- **`/ss [itemName]`**: same as `/sort set ...`
- **`/sort reset [itemName]`**: Delete saved sort position (name optional if holding).
- **`/sort positions`**: List saved sort positions.

## Config / files
All files are created under `BepInEx/config`.
- **Bindings**: `pasta.quicksort.sort.bindings.json`
- **Saved positions**: `pasta.quicksort.sort.positions.json`

## Notes
- `skippedItems` is used as the global skip list for full `/sort` (no item name).  
- For `/sort <itemName>` (move one type), filtering uses the per-item skip logic.

## SS
![alt text](https://raw.githubusercontent.com/P-Asta/lc-QuickSort/refs/heads/main/assets/image.png)