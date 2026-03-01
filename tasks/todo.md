# GV2 — Current Tasks

## Phase 1: Web3 Foundation (Wallet Connect + Balance) ✅ COMPLETE

### Done
- [x] Thirdweb SDK imported
- [x] Server wallet created and funded
- [x] Web3Manager.cs singleton created
- [x] WalletConnectPanel.cs created
- [x] WalletHUD.cs created
- [x] Bootstrap scene setup complete
- [x] End-to-end flow: Bootstrap → Menu → Connect Wallet → Enter Match

---

## Phase 2: Ship NFTs (Ownership Matters) 🔧 IN PROGRESS

### Done
- [x] ShipNFTManager.cs — queries ERC-1155 balances, manages ship ownership + selection
- [x] ShipSelectionUI.cs + ShipCardUI.cs — selection screen with cards, lock overlays
- [x] ShipDefinition class — Inspector-editable ship config (names, rarity, token IDs, mesh index)
- [x] Updated Web3_Integration_Memory.md with Phase 2 status

### Needs Veera in Unity
- [ ] Deploy ERC-1155 contract via Thirdweb dashboard (see deployment guide below)
- [ ] Mint test ships (token IDs 1, 2, 3) via Thirdweb dashboard
- [ ] Add ShipNFTManager component to Bootstrap scene (same GO as Web3Manager)
- [ ] Paste contract address into ShipNFTManager Inspector field
- [ ] Configure 4 ships in Inspector (1 default + 3 NFT ships)
- [ ] Create ship card prefab with ShipCardUI component
- [ ] Set up ShipSelectionUI panel in SCK_MainMenu scene
- [ ] Wire ship selection into WalletConnectPanel flow
- [ ] Test: wallet connect → fetch NFTs → select ship → enter match

---

## Deployment Guide — ERC-1155 Contract via Thirdweb Dashboard

Since the Thirdweb server wallet uses EIP-7702 (which Fuji doesn't support yet),
we deploy using the web dashboard instead. Takes about 2 minutes:

1. Go to https://thirdweb.com/thirdweb.eth/TokenERC1155
2. Click **"Deploy Now"**
3. Fill in:
   - **Name:** GV2 Vimanas
   - **Symbol:** GV2V
   - **Network:** Avalanche Fuji (search "fuji" in the dropdown)
   - **Deployer/Owner:** Should be your connected wallet (use the server wallet or your own)
4. Click **"Deploy"** and confirm the transaction
5. Copy the **contract address** — paste it into ShipNFTManager in Unity Inspector
6. Go to the contract's **"NFTs"** tab in the dashboard
7. Click **"Mint"** three times:
   - Token ID 1: Name "Ship Alpha" (or whatever), supply 10
   - Token ID 2: Name "Ship Beta", supply 10
   - Token ID 3: Name "Ship Gamma", supply 5
8. Done! The Unity code will read these balances automatically.

---

## Part A: Dedicated Server Migration ✅ CODE COMPLETE

### Done (Code Changes)
- [x] A1: Added `GameMode.Server`, `ServerMode` enum, and `IsDedicatedServer` property to NetworkManager.cs
- [x] A2: Guarded `OnPlayerJoined` — no camera setup on dedicated server, no "Client joined" UI
- [x] A3: Updated NetworkedSpaceshipBridge.cs — server treats all ships as remote, no camera/HUD
- [x] A4: Guarded all UI/Camera/Local-Player references across 7 files
- [x] A5: BattleRewardBridge disables itself on dedicated server (Web3 is client-only)
- [x] A6: Created ServerBootstrap.cs (audio off, framerate cap, VSync off)
- [x] A8: Added ServerMode toggle (Host / DedicatedServer / Auto) on NetworkManager Inspector

### Still Needs Veera in Unity
- [ ] Add ServerBootstrap.cs to the first scene that loads (same GO as NetworkManager, or a new one)
- [ ] Test in Unity: Host mode still works exactly as before (no behavior changes)
- [ ] Test with ParrelSync: main editor = Host, clone = Client — verify everything works
- [ ] When ready for VPS: use Unity 6's Dedicated Server build target (File → Build Settings → target)
- [ ] Upload headless build to VPS and launch with: `./GV2Server -batchmode -nographics -server`

---

## Notes
- Ship names/rarity/descriptions are all in the Unity Inspector — NOT on-chain
- The on-chain part is just token IDs and quantities
- Default ship (free, no NFT) uses meshRootIndex 0 (Spaceship / Aircraft A)
- NFT ships can map to meshRootIndex 0 or 1 (or higher when new meshes are added)
- All blockchain calls are async and run in the background — won't freeze the game
