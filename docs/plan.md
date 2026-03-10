# Plan: Add Locked Ship Support

## What Changes

### 1. ShipDefinition (in ShipNFTManager.cs) — add one field
- Add `bool isLocked = false` with tooltip "If true, this ship cannot be selected regardless of ownership (e.g. coming soon, level-gated)"

### 2. ShipNFTManager.cs — gate selection on locked
- `OwnsShip()` → return false if `ship.isLocked` (locked ships are never "available")
- `SelectShip()` → already calls `OwnsShip()`, so it auto-blocks locked ships
- If the currently selected ship gets locked, `SelectedShip` property falls back to default ship (already does this via `?? GetDefaultShip()`)

### 3. ShipCardUI.cs — show locked reason
- Add a `lockedReasonText` field (TMP_Text, optional) to show "Locked" vs "Not Owned"
- `Setup()` gets a new `bool locked` parameter — if locked, show "Locked" text on the lock overlay instead of nothing
- Button stays disabled (already handled by `!owned`)

### 4. ShipSelectionUI.cs — pass locked state to cards
- `PopulateShipCards()` → pass `ship.isLocked` to `ShipCardUI.Setup()`
- `OnShipCardClicked()` → show "This ship is locked" instead of "You don't own this ship" when clicking a locked ship

## Files Touched
- `ShipNFTManager.cs` (ShipDefinition class + OwnsShip)
- `ShipCardUI.cs` (Setup method + optional locked text)
- `ShipSelectionUI.cs` (card population + click message)
