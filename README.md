# MythiX Battle Royale | Akasthara Phase 1 on Avalanche

> Built for the **Avalanche Build Games** hackathon | Main branch: `main` | Development branch: `MythiX_GV`

A multiplayer aerial battle royale built in Unity, with on-chain ship NFTs and play-to-earn token rewards on the **Avalanche C-Chain**. This is Phase 1 of the Akasthara universe, set in the GarudaVana Kingdom.

Ancient Vedic mythology meets high-tech sci-fi: fly **Vimanas** (ships), wield **Astras** (weapons), and earn **PRANA** tokens, all verifiable on-chain.

<!-- Demo video: [TBA] -->

---

## Vision

Akasthara is a mythic-futuristic game universe that begins with aerial combat in the GarudaVana Kingdom and expands into a multi-terrain, multi-vehicle Battle Royale with adventure storytelling and long-term progression. Each phase introduces new kingdoms, vehicle types, combat perspectives, and narrative layers. MythiX is the first playable face of this world: a competitive 4-player battle royale where blockchain is invisible infrastructure. Players never feel like they are using crypto. Wallets are abstracted, onboarding is frictionless, and ownership engagement is optional until the player is ready.

---

## What It Does

Players connect their wallet, select a ship and characters they own as NFTs, and battle in a shrinking-zone arena. The prototype supports 4 players per match, scaling to 100 players for full launch. When the match ends, the game automatically mints ERC-20 reward tokens to each player based on their final placement. No claiming UI, no manual transactions, just play and earn.

**Gameplay Flow:**
1. Connect wallet (email, Google, Discord, MetaMask, or guest)
2. Select ship (ownership verified on-chain via ERC-1155)
3. Select characters from two rosters with rarity-based stat multipliers
4. Battle in multi-terrain arenas flying Vimana spaceships
4. Shrinking battle zone forces combat, last ship standing wins
5. Match ends and PRANA tokens are minted directly to player wallets
6. Post-match screen shows on-chain earnings + tx confirmation

---

## Avalanche On-Chain Integration

### Smart Contracts (Fuji Testnet, Chain ID `43113`)

| Contract | Type | Address | Verify |
|----------|------|---------|--------|
| Ship NFTs | ERC-1155 | `0x8405209745b8f1A43D21876120543d20e4a7600C` | [View on Snowtrace](https://testnet.snowtrace.io/address/0x8405209745b8f1A43D21876120543d20e4a7600C) |
| PRANA Token | ERC-20 | `0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1` | [View on Snowtrace](https://testnet.snowtrace.io/address/0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1) |
| Server Wallet | EOA | `0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f` | [View on Snowtrace](https://testnet.snowtrace.io/address/0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f) |
| Reward Wallet | EOA (Minter) | `0x78A75F10f4c2A20bd30c4A683607ABc1A22Bb352` | [View on Snowtrace](https://testnet.snowtrace.io/address/0x78A75F10f4c2A20bd30c4A683607ABc1A22Bb352) |

All contracts are deployed and tested on the Avalanche Fuji C-Chain. The reward wallet holds the Minter role on the PRANA token contract. You can verify minting transactions on Snowtrace.

### On-Chain Features

**Wallet Authentication** - Players connect via email, Google, Discord, guest login, or external wallets (MetaMask/Coinbase). Powered by Thirdweb InAppWallet + Reown AppKit.

**Ship and Character NFTs (ERC-1155)** - Ships and characters are on-chain tokens on the Avalanche C-Chain. The game queries the player's NFT balance to determine which ships they can fly and which characters they can use. 8 characters across 2 rosters with rarity tiers (Common, Rare, Epic, Legendary) that apply stat multipliers. Free defaults are available so the game is accessible without holding NFTs.

**Battle Rewards (ERC-20)** - When a match ends, a dedicated reward wallet mints PRANA tokens to each player based on their final placement (1st through 4th). Minting happens asynchronously via the Avalanche Fuji RPC so it never blocks the game loop. Transactions are confirmed on-chain before the UI updates.

**Live Balance Display** - The in-game HUD shows the player's AVAX balance and PRANA token balance in real-time, fetched directly from the Avalanche C-Chain.

### Why Avalanche?

Avalanche's design philosophy aligns with how we want players to experience blockchain: fast, cheap, and invisible.

- **Low gas fees** - minting rewards costs ~0.006 AVAX per transaction, making per-match token distribution viable
- **Fast finality** - Fuji confirms transactions in 1 to 2 seconds, so players see rewards almost instantly
- **C-Chain EVM compatibility** - standard ERC-20/ERC-1155 contracts work out of the box with Thirdweb + Nethereum
- **Invisible Web3** - players connect with email or Google, a wallet is created behind the scenes, and they never touch gas or seed phrases unless they choose to

### Web3 Architecture

All blockchain code lives in `Assets/GV/Scripts/Web3/` under the `GV.Web3` namespace, cleanly separated from game networking code (`GV.Network`).

- **Async/await everywhere** - blockchain calls never freeze the game
- **Nethereum for minting** - Thirdweb SDK handles reads; Nethereum handles write transactions because Thirdweb's PrivateKeyWallet isn't available in Unity builds
- **Fresh Web3 instance per mint** - prevents stale nonce errors that cause failed transactions
- **Event-driven** - Web3 systems fire events (OnWalletConnected, OnRewardsDistributed, etc.) that UI and game systems subscribe to

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Blockchain | Avalanche C-Chain, Fuji Testnet (43113) |
| Web3 SDK | Thirdweb Unity SDK |
| Transaction Signing | Nethereum v5.0.0 |
| External Wallets | Reown AppKit (MetaMask, Coinbase, WalletConnect) |
| Smart Contracts | ERC-1155 (ships) + ERC-20 (PRANA rewards) |
| Engine | Unity 6000.3.0f1, Universal Render Pipeline |
| Multiplayer | Photon Fusion 2 (Host/Client, max 4 players) |
| Vehicle Framework | SpaceCombatKit |

---

## Project Structure

```
Assets/GV/Scripts/
├── Web3/                  <- Avalanche blockchain integration (13 scripts)
│   ├── Web3Manager.cs         Wallet connect singleton (InApp + External)
│   ├── WalletConnectPanel.cs  Login UI (Email/Google/Discord/Guest/MetaMask)
│   ├── WalletHUD.cs           In-game balance display (AVAX + PRANA)
│   ├── ShipNFTManager.cs      ERC-1155 ship ownership queries
│   ├── CharacterNFTManager.cs ERC-1155 character ownership queries
│   ├── ShipSelectionUI.cs     Ship selection screen
│   ├── CharacterSelectionUI.cs Character selection screen
│   ├── ShipCardUI.cs          Individual ship card component
│   ├── CharacterCardUI.cs     Individual character card component
│   ├── BattleRewardManager.cs ERC-20 token minting via Nethereum
│   ├── BattleRewardBridge.cs  Connects match events to reward distribution
│   ├── PostMatchRewardUI.cs   End-of-match earnings display
│   └── Web3Bootstrap.cs       Scene initialization
│
├── Network/               <- Photon Fusion 2 multiplayer (23 scripts)
│   ├── NetworkManager.cs      Session management, player spawning
│   ├── NetworkedGameManager.cs Match state machine
│   ├── NetworkedSpaceshipBridge.cs Ship sync (position, health, weapons)
│   ├── EliminationTracker.cs  Death tracking, placement assignment
│   ├── BattleZoneController.cs Shrinking zone logic
│   ├── RoomManager.cs         Dedicated server room management via HTTP API
│   └── ...
│
├── AI/                    <- Enemy spaceship behaviors
├── Combat/                <- Weapon and damage systems
├── Vehicle/               <- Vimana ship systems
└── Teleport/              <- Dice-based teleportation mechanic
```

---

## Team

| Role | Name | Notes |
|------|------|-------|
| Founder | Bharath Vizaveera (Veera) | Filmmaker, Web3 builder, and entrepreneur |
| Game Developer | Sai Bharath Kalyan | Unity development |
| Game Developer | Raj Agrahari | Unity development |

---

## Setup

### Prerequisites
- Unity 6000.3.0f1 (URP)
- Photon Fusion 2 AppID (set in Fusion Hub)
- Thirdweb Client ID (set on ThirdwebManager prefab in Inspector)

### Running Locally
1. Clone the repo (latest code is on `main`, development branch is `MythiX_GV`)
2. Open in Unity and load `Assets/GV/Scenes/Bootstrap.unity` (entry scene)
3. Set your Thirdweb Client ID on the `ThirdwebManager` prefab
4. Enter Play mode: Bootstrap -> Main Menu -> Connect Wallet -> Match

### Avalanche Testnet Setup
- Network: Avalanche Fuji C-Chain (Chain ID `43113`)
- RPC: `https://api.avax-test.network/ext/bc/C/rpc`
- Faucet: [Avalanche Fuji Faucet](https://faucet.avax.network/)
- The reward wallet needs ~0.01 test AVAX to pay gas for minting rewards

---

## Roadmap

| Timeline | Milestone | Details |
|----------|-----------|---------|
| 2026 Q1 | Battle Royale Pre-Alpha | Core combat mechanics, multiplayer systems, Avalanche Build Games Stage 2 MVP |
| 2026 Q2 | Beta Release | Community playtesting, social media, influencer outreach, wishlist campaigns |
| 2026 Q3 | Akasthara Phase 1 Launch | Full GarudaVana aerial BR experience, go-to-market push, seed fundraising begins |
| 2026 Q4 | Adventure RPG Initiation | Narrative designers, world builders, deeper lore, quests, character progression |
| 2027 Q1 | Phase 2 Expansion | New regions, new vehicle types, interconnected BR and adventure systems |

---

## What's Next

- [x] Wallet connect + balance display on Avalanche C-Chain
- [x] Ship NFTs (ERC-1155 ownership-gated selection)
- [x] Character NFTs (ERC-1155 with rarity-based stat multipliers)
- [x] Battle rewards (ERC-20 minting on match end)
- [ ] Wallet identity linked to multiplayer sessions
- [ ] On-chain match results / leaderboard on Avalanche
- [ ] Marketplace / ship and character trading
