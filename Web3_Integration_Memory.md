# GV2 — Web3 Integration Memory

> Persistent context file for the thirdweb + Avalanche blockchain integration into Prototype_GV2.
> Last updated: Feb 27, 2026

---

## 1. Project Identity

- **Project:** Prototype_GV2 (Mythic Futurism Sci-Fi Multi-Terrain Vehicle Battle Royale)
- **Engine:** Unity 6000.3.0f1, URP
- **Theme:** Ancient Vedic mythology meets high-tech sci-fi — Vimanas (ships), Astras (weapons), Pranas (power-ups)
- **Multiplayer:** Photon Fusion 2 (Host/Client, RPC-based input, max 4 players)
- **Primary Namespace:** `GV.Network`

---

## 2. Blockchain Stack

| Layer | Technology | Status |
|-------|-----------|--------|
| Blockchain | Avalanche C-Chain | Fuji Testnet (43113) |
| SDK (Unity) | Thirdweb Unity SDK | Imported to `Assets/Thirdweb/` — not yet integrated into game loop |
| SDK (Backend) | Thirdweb MCP Server | Connected — wallet, contract, token operations available |
| Docs | Avalanche MCP Server | Connected — full docs/academy/integrations access |
| Server Wallet | `0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f` | Funded with 2 AVAX on Fuji |
| Smart Wallet | `0x2355...03E7` (paired with server wallet) | Active |

---

## 3. Thirdweb Unity SDK Structure

```
Assets/Thirdweb/
├── Runtime/
│   ├── NET/           → Core DLLs (Thirdweb.dll, Nethereum, BouncyCastle, Newtonsoft.Json)
│   └── Unity/         → ThirdwebManager, ThirdwebManagerBase, ThirdwebManagerServer
│       ├── Browser/   → Android, iOS, WebGL browser integrations
│       ├── Http/      → CrossPlatformUnityHttpClient
│       └── Wallets/   → InAppWallet, EcosystemWallet, ReownWallet (external connect)
├── Editor/            → ThirdwebManagerEditor, Postprocessor
├── Examples/          → PlaygroundManager.cs (demo: wallet connect, sign, balance, transfer, contract R/W)
├── Plugins/           → WebGL threading patcher, input copy
├── Resources/         → Editor banner
└── Prefabs/           → ThirdwebManager.prefab, ThirdwebManagerServer.prefab, DefaultOTPModal.prefab
```

**ThirdwebManager requires:**
- `ClientId` — from https://thirdweb.com/create-api-key (set in Inspector)
- `BundleId` — auto-derived from Application.identifier if blank
- Optional `RpcOverrides` — for custom Avalanche RPC endpoints

---

## 4. Game Architecture (Key Systems)

### Networking (Photon Fusion 2)
- Photon AppID: `4f22d424-faf1-48c0-a0f6-ef7db60063dc`
- Region: US | Session: `GV_Battle` | Max Players: 4
- RPC-based input pipeline (workaround for Fusion OnInput issues)
- Key scripts: `NetworkManager.cs`, `NetworkedSpaceshipBridge.cs`, `NetworkedPlayerInput.cs`, `NetworkedGameManager.cs`
- Game states: Waiting → Countdown → Battle → Finished

### Combat & Weapons
- `MissileCycleControllerDynamic.cs` — manages missile inventory (Astras)
- Modified SCK missile/projectile system for network-aware targeting
- Power-ups (Pranas): SuperBoost, Shield, Invisibility, Teleport, SuperWeapon, Random

### Vehicles
- SpaceCombatKit VehicleEngines3D for 3D Vimana flight
- Aircraft mesh swap system (A/B variants, networked via `SyncIsAActive`)
- Characters: Atom Rider, Sarathi (pilots)

### Battle Royale System
- Multi-terrain arenas with checkpoint progression
- Teleportation dice mechanic
- Shrinking zone (`BattleZoneController.cs`) — last vehicle standing wins

---

## 5. Folder Structure (Game Code)

```
Assets/GV/
├── Scripts/
│   ├── Network/        → 18 files — Fusion networking core
│   ├── Battle Network/  → 10 files — checkpoints, zones, rings
│   ├── AI/             → 3 files — attack, combat, evasion behaviors
│   ├── Character/      → pilot management
│   ├── Teleport/       → dice-based teleportation
│   ├── Swap/           → aircraft mesh/engine swapping
│   └── Debug/          → in-game debug tools
├── Prefabs_GV/
│   ├── Network/        → Player_Network Manager, Player_ForMultiplayer, Gyro_ForNetwork, LevelSync
│   └── (power-up gyros, weapon cubes, mounts)
├── Characters/         → Atom Rider, Sarathi
├── Materials/, Meshes/, HDRI/, Fonts/, Audio/, Ui/
└── Documentation/      → Multiplayer sync fix notes
```

---

## 6. Web3 Integration Status

### Done
- [x] Thirdweb Unity SDK imported
- [x] Thirdweb MCP server connected and operational
- [x] Avalanche MCP server connected and operational
- [x] Server wallet created and funded (2 AVAX on Fuji 43113)
- [x] Smart wallet paired with server wallet

### Not Started
- [ ] ThirdwebManager prefab configured with ClientId in scene
- [ ] Player wallet connection flow (InAppWallet / EcosystemWallet / External)
- [ ] Smart contract design (NFTs for ships/Astras/skins? ERC-20 token? Battle rewards?)
- [ ] Contract deployment on Fuji testnet
- [ ] In-game wallet UI (connect, balance, inventory)
- [ ] Linking wallet identity to Photon Fusion player session
- [ ] On-chain match results / leaderboard
- [ ] NFT-gated content (ships, skins, weapons)
- [ ] Token reward distribution post-match
- [ ] Marketplace / trading system

---

## 7. Integration Touchpoints

These are the game systems where Web3 will connect:

| Game System | Web3 Feature | Integration Point |
|-------------|-------------|-------------------|
| Player join (NetworkManager) | Wallet connect | Before Fusion session join |
| Ship selection | NFT ownership check | Gate ship prefabs by NFT |
| Power-ups (Pranas) | Token-gated or NFT power-ups | `PowerSphereMasterController` |
| Match end (NetworkedGameManager) | Token reward distribution | On `GameState.Finished` |
| Leaderboard | On-chain match results | Post-match submission |
| Cosmetics | NFT skins/trails | Mesh swap system (`AircraftMeshSwap`) |
| Weapons (Astras) | NFT weapon variants | `MissileCycleControllerDynamic` |

---

## 8. Key Dependencies

- Photon Fusion 2 (networking)
- SpaceCombatKit (vehicles, weapons, combat)
- Unity Input System 1.16.0
- Cinemachine 2.10.5
- URP 17.3.0
- Unity Splines 2.8.2 (terrain paths / arena layouts)
- Unity Purchasing 4.14.0 (future monetization)
- ParrelSync (dual-instance testing)

---

## 9. Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| Feb 27, 2026 | Use Avalanche Fuji testnet (43113) | Free test tokens, fast finality, low fees for game transactions |
| Feb 27, 2026 | Thirdweb as primary Web3 SDK | Already imported, strong Unity support, managed wallets, gasless tx capability |

---

## 10. Integration Roadmap

### Phase 1 — Foundation (Wire Up the Basics)
> Goal: Prove a player can connect a wallet and see their balance before entering a match.

- [ ] Configure ThirdwebManager prefab with ClientId in a bootstrap/persistent scene
- [ ] Create `GV.Web3.Web3Manager.cs` singleton — initializes before NetworkManager, holds player wallet reference
- [ ] Build wallet connect screen using InAppWallet (email/social login — no MetaMask needed)
- [ ] Display connected wallet address and AVAX balance in HUD
- [ ] All calls target Fuji testnet (43113)
- **Key files:** ThirdwebManager.prefab, new `Web3Manager.cs`, new wallet connect UI panel
- **Integration point:** Wallet connect happens before Fusion session join in NetworkManager flow

### Phase 2 — Ship NFTs (Ownership Matters)
> Goal: Players own ships as NFTs. Ship selection is gated by what you hold in your wallet.

- [ ] Deploy ERC-1155 contract on Fuji for Vimana ships (1155 allows unique 1-of-1s and edition variants)
- [ ] Mint test ships from server wallet
- [ ] After wallet connect, query player's NFT balance and populate ship selection
- [ ] Players without NFTs get a default free ship
- [ ] Map each token ID to a ship prefab/mesh variant
- **Key files:** ERC-1155 contract, new `ShipNFTManager.cs`, hooks into `AircraftMeshSwap.cs`
- **Integration point:** Ship selection screen between wallet connect and match join

### Phase 3 — Battle Rewards (Earn by Playing)
> Goal: Players earn fungible tokens for competing. Better placement = bigger reward.

- [ ] Deploy ERC-20 token contract on Fuji (game currency — PRANA, ASTRA, or lore-appropriate name)
- [ ] On `NetworkedGameManager.GameState.Finished`, host submits match results
- [ ] Server wallet distributes tokens based on placement (winner gets most, all survivors earn something)
- [ ] Display earnings in post-match summary screen
- **Key files:** ERC-20 contract, new `BattleRewardManager.cs`, hooks into `NetworkedGameManager.cs`
- **Integration point:** Triggered by game state transition to Finished

### Phase 4 — Weapon & Power-Up NFTs (Deeper Gameplay Integration)
> Goal: Astras and Pranas become collectible NFTs that affect gameplay.

- [ ] Expand 1155 contract (or deploy new one) for Astra weapon variants and special Prana power-ups
- [ ] NFT-gated weapon types — owning "Agni Astra" NFT unlocks fire missile in `MissileCycleControllerDynamic`
- [ ] Rare Prana variants (stronger shield, longer invisibility) tied to NFT ownership
- [ ] ERC-20 token sink — players can spend tokens to buy packs/crates
- **Key files:** Updated 1155 contract, new `WeaponNFTManager.cs`, hooks into `MissileCycleControllerDynamic.cs`, `PowerSphereMasterController.cs`
- **Integration point:** Loadout selection and in-match power-up activation

### Phase 5 — Smart Wallet & Gasless Experience
> Goal: Players never see gas fees. Blockchain is invisible to the end user.

- [ ] Upgrade player wallets to thirdweb smart wallets with gas sponsorship
- [ ] Server wallet covers all transaction costs for players
- [ ] Add session keys so the game can sign transactions mid-match without popup interruptions
- [ ] Players interact with Web3 features without needing to understand blockchain
- **Key files:** Updated `Web3Manager.cs`, ThirdwebManager smart wallet config
- **Integration point:** Applied globally across all wallet interactions

### Phase 6 — On-Chain Leaderboard & Tournaments
> Goal: Immutable rankings and competitive staked tournaments.

- [ ] Deploy leaderboard contract — stores match results on-chain (verifiable, tamper-proof)
- [ ] Build tournament system — players stake ERC-20 tokens to enter, winner takes pool (minus treasury fee)
- [ ] Tournament mode ties into `BattleZoneController` for Battle Royale with real stakes
- [ ] Display on-chain rankings in-game
- **Key files:** Leaderboard contract, Tournament contract, new `TournamentManager.cs`, hooks into `BattleZoneController.cs`
- **Integration point:** New game mode alongside existing battle flow

### Phase 7 — Marketplace & Trading
> Goal: Players can trade ships, weapons, and cosmetics with each other.

- [ ] Deploy or integrate thirdweb marketplace contract
- [ ] In-game marketplace UI — list, buy, trade ship NFTs and weapon NFTs using ERC-20 token
- [ ] Add cosmetic NFTs (trails, exhaust effects, pilot skins) for `AircraftMeshSwap` and character systems
- [ ] Option to link to external marketplace for broader trading
- **Key files:** Marketplace contract, new `MarketplaceManager.cs`, new marketplace UI panels
- **Integration point:** Accessible from main menu and post-match screens

---

## 12. Open Questions

1. **What NFT standard for ships?** ERC-721 (unique ships) vs ERC-1155 (editions/variants)?
2. **Token economy design?** Is there a fungible game token, or purely NFT-based?
3. **Gasless transactions?** Use thirdweb smart wallet to sponsor gas for players?
4. **Wallet flow?** InAppWallet (email/social login, no MetaMask needed) vs external wallet?
5. **On-chain vs off-chain?** What data goes on-chain (match results, ownership) vs stays off-chain (battle state, positions)?
6. **Marketplace scope?** In-game trading, or link to external marketplace?

---

## 13. Notes for Future Sessions

- Always target **chain ID 43113** (Fuji testnet) for all contract deployments and transactions
- Server wallet address: `0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f`
- The Photon Fusion input system uses a custom RPC workaround — any Web3 calls must be async and not block the Fusion tick
- ThirdwebManager is a singleton — place it in the first loaded scene or a persistent bootstrap scene
- The game uses `GV.Network` namespace for all custom network code — Web3 scripts should use a `GV.Web3` or `GV.Blockchain` namespace
