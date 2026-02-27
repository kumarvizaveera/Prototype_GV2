# Phase 1 Setup Guide — Wallet Connect + Balance

This guide walks you through connecting the new Web3 scripts to your Unity scenes.
All the code is written — these are the Unity Editor steps to wire it all together.

---

## Step 1: Get a Thirdweb Client ID

You need an API key from Thirdweb so the SDK can talk to the blockchain.

1. Go to https://thirdweb.com/create-api-key
2. Sign in (or create a free account)
3. Create a new API key — name it something like "GV2_Dev"
4. Copy the **Client ID** (a long string like `abc123def456...`)
5. Keep this page open — you'll paste this into Unity in Step 3

---

## Step 2: Create the Bootstrap Scene

This is a tiny scene that loads first, sets up the blockchain managers, then moves to your menu.

1. In Unity: **File → New Scene → Empty Scene**
2. Save it as `Bootstrap` in your Scenes folder (e.g. `Assets/GV/Scenes/Bootstrap.unity`)
3. In this empty scene, create these GameObjects:

### A. ThirdwebManager
- In the Project panel, find: `Assets/Thirdweb/Runtime/Unity/Prefabs/ThirdwebManager.prefab`
- Drag it into the Bootstrap scene hierarchy
- Select it → in the Inspector, paste your **Client ID** into the `Client Id` field
- The `Bundle Id` can stay blank (it auto-detects)

### B. Web3Manager
- In the Hierarchy: **right-click → Create Empty** → name it `Web3Manager`
- In the Inspector: **Add Component → search "Web3Manager"** (it's in the GV.Web3 namespace)
- Settings should show:
  - Chain Id: `43113` (already defaulted — this is Fuji testnet)
  - Default Auth Provider: `Google` (or whatever you prefer for testing)

### C. Web3Bootstrap
- Select the `Web3Manager` object (or create another empty)
- **Add Component → search "Web3Bootstrap"**
- Set `Next Scene Name` to the name of your menu scene (see Step 3)
- Check `Auto Load Next Scene` = true

---

## Step 3: Set Up the Menu Scene

You have two options:

### Option A: Use the existing SCK Main Menu scene
- Find `Assets/SpaceCombatKit/SpaceCombatKit/Scenes/MainScenes/SCK_MainMenu.unity`
- Open it — this already has the Menus_InputSystem_SCK prefab
- You'll add the wallet connect panel here (Step 4)
- In Web3Bootstrap, set Next Scene Name to `SCK_MainMenu`

### Option B: Create a fresh menu scene
- **File → New Scene → Basic (Built-in)**
- Save as `MainMenu` in `Assets/GV/Scenes/`
- Drag in the `Menus_InputSystem_SCK` prefab from `Assets/SpaceCombatKit/SpaceCombatKit/Prefabs/`
- Also drag in the `GameStateManager` prefab from the same folder
- In Web3Bootstrap, set Next Scene Name to `MainMenu`

---

## Step 4: Add the Wallet Connect Panel to Your Menu

This is the UI where players connect their wallet.

1. In your menu scene, create a UI Canvas:
   - **Right-click in Hierarchy → UI → Canvas**
   - Set Canvas Scaler to **Scale With Screen Size**, Reference Resolution: 1920×1080

2. Inside the Canvas, create the panel:
   - **Right-click Canvas → UI → Panel** → name it `WalletConnectPanel`
   - Make it a reasonable size (e.g. 500×400, centered)

3. Add child elements inside the panel:
   - **UI → Text - TextMeshPro** → name it `TitleText`, set text to "Connect Your Wallet"
   - **UI → Input Field - TextMeshPro** → name it `EmailInput`, set placeholder to "Enter email..."
   - **UI → Button - TextMeshPro** → name it `ConnectEmailBtn`, set text to "Connect with Email"
   - **UI → Button - TextMeshPro** → name it `ConnectGoogleBtn`, set text to "Sign in with Google"
   - **UI → Button - TextMeshPro** → name it `ConnectGuestBtn`, set text to "Play as Guest"
   - **UI → Text - TextMeshPro** → name it `StatusText` (for showing "Connecting..." messages)

4. Add the WalletConnectPanel script:
   - Select the `WalletConnectPanel` object
   - **Add Component → search "WalletConnectPanel"**
   - Drag the UI elements into their slots:
     - Panel → the WalletConnectPanel object itself
     - Email Input Field → EmailInput
     - Status Text → StatusText
     - Title Text → TitleText
     - Connect Email Button → ConnectEmailBtn
     - Connect Google Button → ConnectGoogleBtn
     - Connect Guest Button → ConnectGuestBtn

---

## Step 5: Add the Wallet HUD to the Gameplay Scene

This shows the wallet address and balance during a match.

1. Open your gameplay scene (`MP_Mechanics_6`)
2. Find or create a HUD Canvas
3. Add a small panel in the top-right corner:
   - **UI → Panel** → name it `WalletHUD`, anchor to top-right, size ~300×80
   - Add two **TMP_Text** children:
     - `AddressText` — for the short wallet address
     - `BalanceText` — for the AVAX balance
4. Add the WalletHUD script:
   - Select `WalletHUD`
   - **Add Component → search "WalletHUD"**
   - Drag the elements into their slots

---

## Step 6: Update Build Settings

The Bootstrap scene must load first.

1. **File → Build Settings**
2. Add scenes in this order:
   - **Scene 0:** Bootstrap
   - **Scene 1:** Your menu scene (SCK_MainMenu or MainMenu)
   - **Scene 2:** MP_Mechanics_6 (gameplay)
3. Make sure all three are **enabled** (checkbox checked)

---

## Step 7: Test It

1. **Play the Bootstrap scene** (make sure it's the active scene in the editor)
2. It should automatically load into your menu scene
3. You should see the wallet connect panel
4. Click "Play as Guest" for the quickest test
5. If it works, you'll see the wallet address appear
6. The balance should show (it'll be 0 AVAX for a guest wallet — that's normal)

### Troubleshooting
- **"ThirdwebManager not found"** → Make sure the ThirdwebManager prefab is in the Bootstrap scene
- **"Invalid ClientId"** → Double-check your Client ID from thirdweb.com
- **Buttons don't work** → Make sure the WalletConnectPanel script has all button references assigned
- **Nothing happens on social login** → Social login opens a browser window — check if a popup was blocked

---

## What Each Script Does (Summary)

| Script | Purpose | Where It Lives |
|--------|---------|---------------|
| `Web3Bootstrap.cs` | Checks managers exist, loads menu scene | Bootstrap scene |
| `Web3Manager.cs` | The brain — connects wallets, fetches balance | Bootstrap scene (persists) |
| `WalletConnectPanel.cs` | The login UI — buttons, email field, status | Menu scene |
| `WalletHUD.cs` | Shows address + balance during gameplay | Gameplay scene |

---

## Files Created

```
Assets/GV/Scripts/Web3/
├── Web3Manager.cs         — Singleton, wallet logic, events
├── Web3Bootstrap.cs       — Bootstrap scene setup
├── WalletConnectPanel.cs  — Wallet connect UI panel
└── WalletHUD.cs           — In-game wallet display
```
