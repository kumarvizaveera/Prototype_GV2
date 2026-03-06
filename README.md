# Prototype GV2 — Mythic Futurism Battle Royale on Avalanche

> Built for the **Avalanche Build Games** hackathon

A multiplayer sci-fi vehicle battle royale built in Unity, with on-chain ship NFTs and play-to-earn token rewards on the **Avalanche C-Chain**.

Ancient Vedic mythology meets high-tech sci-fi: fly **Vimanas** (ships), wield **Astras** (weapons), and earn **PRANA** tokens — all verifiable on-chain.

---

## What It Does

Players connect their wallet, select a ship they own as an NFT, and battle in a 4-player shrinking-zone arena. When the match ends, the game automatically mints ERC-20 reward tokens to each player based on their final placement — no claiming UI, no manual transactions, just play and earn.

**Gameplay Flow:**
1. Connect wallet (email, Google, Discord, MetaMask, or guest)
2. Select ship — ownership verified on-chain via ERC-1155
3. Battle in multi-terrain arenas flying Vimana spaceships
4. Shrinking battle zone forces combat — last ship standing wins
5. Match ends → PRANA tokens minted directly to player wallets
6. Post-match screen shows on-chain earnings + tx confirmation

---

## Blockchain Integration

### Smart Contracts (Fuji Testnet)

| Contract | Type | Address |
|----------|------|---------|
| Ship NFTs | ERC-1155 | `0x8405209745b8f1A43D21876120543d20e4a7600C` |
| PRANA Token | ERC-20 | `0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1` |

### On-Chain Features

**Wallet Authentication** — Players connect via email, Google, Discord, guest login, or external wallets (MetaMask/Coinbase). Powered by Thirdweb InAppWallet + Reown AppKit.

**Ship NFTs (ERC-1155)** — Each ship type is an on-chain token. The game queries the player's NFT balance to determine which ships they can fly. A free default ship is available to all players so the game is accessible without holding NFTs.

**Battle Rewards (ERC-20)** — When a match ends, a dedicated reward wallet mints PRANA tokens to each player based on their final placement (1st–4th). Minting happens asynchronously so it never blocks the game loop. Transactions are confirmed on-chain before the UI updates.

**Live Balance Display** — The in-game HUD shows the player's AVAX balance and PRANA token balance in real-time, fetched directly from the Avalanche C-Chain.

### Web3 Architecture

All blockchain code lives in `Assets/GV/Scripts/Web3/` under the `GV.Web3` namespace, cleanly separated from game networking code (`GV.Network`).

- **Async/await everywhere** — blockchain calls never freeze the game
- **Nethereum for minting** — Thirdweb SDK handles reads; Nethereum handles write transactions because Thirdweb's PrivateKeyWallet isn't available in Unity builds
- **Fresh Web3 instance per mint** — prevents stale nonce errors that cause failed transactions
- **Event-driven** — Web3 systems fire events (`OnWalletConnected`, `OnRewardsDistributed`, etc.) that UI and game systems subscribe to

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Blockchain | Avalanche C-Chain — Fuji Testnet (43113) |
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
├── Web3/                  ← Blockchain integration (10 scripts)
│   ├── Web3Manager.cs         Wallet connect singleton (InApp + External)
│   ├── WalletConnectPanel.cs  Login UI (Email/Google/Discord/Guest/MetaMask)
│   ├── WalletHUD.cs           In-game balance display (AVAX + PRANA)
│   ├── ShipNFTManager.cs      ERC-1155 ship ownership queries
│   ├── ShipSelectionUI.cs     Ship selection screen
│   ├── ShipCardUI.cs          Individual ship card component
│   ├── BattleRewardManager.cs ERC-20 token minting via Nethereum
│   ├── BattleRewardBridge.cs  Connects match events → reward distribution
│   ├── PostMatchRewardUI.cs   End-of-match earnings display
│   └── Web3Bootstrap.cs       Scene initialization
│
├── Network/               ← Photon Fusion 2 multiplayer (21 scripts)
│   ├── NetworkManager.cs      Session management, player spawning
│   ├── NetworkedGameManager.cs Match state machine
│   ├── NetworkedSpaceshipBridge.cs Ship sync (position, health, weapons)
│   ├── EliminationTracker.cs  Death tracking, placement assignment
│   ├── BattleZoneController.cs Shrinking zone logic
│   └── ...
│
├── AI/                    ← Enemy spaceship behaviors
├── Combat/                ← Weapon and damage systems
├── Vehicle/               ← Vimana ship systems
└── Teleport/              ← Dice-based teleportation mechanic
```

---

## Setup

### Prerequisites
- Unity 6000.3.0f1 (URP)
- Photon Fusion 2 AppID (set in Fusion Hub)
- Thirdweb Client ID (set on ThirdwebManager prefab in Inspector)

### Running Locally
1. Clone the repo and open in Unity
2. Open `Assets/GV/Scenes/Bootstrap.unity` — this is the entry scene
3. Set your Thirdweb Client ID on the `ThirdwebManager` prefab
4. Enter Play mode — the game loads Bootstrap → Main Menu → Connect Wallet → Match

### Testnet Setup
- Network: Avalanche Fuji C-Chain (Chain ID `43113`)
- RPC: `https://api.avax-test.network/ext/bc/C/rpc`
- Faucet: [Avalanche Fuji Faucet](https://faucet.avax.network/)
- The reward wallet needs a small amount of test AVAX (~0.01) to pay gas for minting

---

## What's Next

- [x] Wallet connect + balance display
- [x] Ship NFTs (ERC-1155 ownership-gated selection)
- [x] Battle rewards (ERC-20 minting on match end)
- [ ] Wallet identity linked to multiplayer sessions
- [ ] On-chain match results / leaderboard
- [ ] Marketplace / ship trading
