# MythiX Battle Royale - Stage 3 Context (Part 1: Overview, Web3 and Blockchain)

## Project Identity

- Project Name: MythiX Battle Royale
- Repo: Prototype_GV2
- Active Branch: MythiX_GV (never merged to Main)
- Engine: Unity 6000.3.0f1 with Universal Render Pipeline (URP 17.3.0)
- Product Version: 2.9.6
- Theme: Mythic Futurism. Ancient Vedic mythology meets sci-fi. Ships are called Vimanas, weapons are Astras, reward tokens are PRANA.
- Built for: Avalanche Build Games hackathon

---

## Blockchain: Avalanche C-Chain (Fuji Testnet)

Chain ID: 43113
RPC URL: https://api.avax-test.network/ext/bc/C/rpc

### Smart Contracts

Ship and Character NFTs (ERC-1155): 0x8405209745b8f1A43D21876120543d20e4a7600C
Snowtrace: https://testnet.snowtrace.io/address/0x8405209745b8f1A43D21876120543d20e4a7600C

PRANA Battle Reward Token (ERC-20): 0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1
Snowtrace: https://testnet.snowtrace.io/address/0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1

Server Wallet (EOA): 0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f
Snowtrace: https://testnet.snowtrace.io/address/0x2bBc1C32224a347eaF8d10cAFaF77F3aBCA2551f

Reward Wallet (EOA, holds Minter role on PRANA contract): 0x78A75F10f4c2A20bd30c4A683607ABc1A22Bb352
Snowtrace: https://testnet.snowtrace.io/address/0x78A75F10f4c2A20bd30c4A683607ABc1A22Bb352

### On-Chain Flow

1. Player connects wallet via Thirdweb InAppWallet or Reown AppKit (external wallets)
2. Game queries ERC-1155 balanceOf to determine which ships and characters the player owns
3. Players who don't own NFTs get a free default ship and default characters
4. Match plays out with up to 4 players
5. When match ends, BattleRewardManager creates a fresh Nethereum Web3 instance using the reward wallet's private key
6. Calls mintTo(playerAddress, amount) on the PRANA ERC-20 contract for each player based on placement
7. Polls for transaction confirmation (up to 30 attempts, 1 second apart)
8. UI updates with confirmed on-chain balance

### Reward Tiers (Per Match)

| Placement | PRANA Tokens | XP  | Energy | Gems | Coins |
|-----------|-------------|-----|--------|------|-------|
| 1st       | 100         | 500 | 50     | 10   | 200   |
| 2nd       | 60          | 300 | 30     | 5    | 120   |
| 3rd       | 30          | 150 | 15     | 2    | 60    |
| 4th       | 10          | 50  | 5      | 0    | 20    |

### Why Nethereum Instead of Thirdweb for Minting

Thirdweb Unity SDK v6 does not expose PrivateKeyWallet in Unity builds. The game needs a server-side wallet to mint tokens without prompting the player for gas. Solution: use Nethereum v5.0.0 directly for write transactions (mintTo), while Thirdweb handles all read operations (balanceOf, wallet connect, balance queries).

### Why Fresh Web3 Instance Per Mint

During playtesting, rapid sequential mints (4 players at match end) caused stale nonce errors when reusing the same Nethereum Web3 instance. Creating a new Account and Web3 object per transaction eliminates this.

### Gas Cost

Approximately 0.006 AVAX per mint transaction on Fuji. Transactions confirm in 1 to 2 seconds.

---

## Web3 SDK Stack

| Component | Version | Purpose |
|-----------|---------|---------|
| Thirdweb Unity SDK | v6 | Wallet connect (InAppWallet), NFT read queries, balance reads |
| Nethereum | v5.0.0 | ERC-20 mintTo() write transactions (signing + sending) |
| Reown AppKit | v1.5.2 | External wallet support (MetaMask, Coinbase Wallet, WalletConnect) |

### Authentication Methods (5 total)

1. Email (via Thirdweb InAppWallet)
2. Google OAuth (via Thirdweb InAppWallet)
3. Discord OAuth (via Thirdweb InAppWallet)
4. Guest (one-tap, no credentials, via Thirdweb InAppWallet)
5. External Wallet (MetaMask, Coinbase Wallet, WalletConnect via Reown AppKit)

---

## Web3 Scripts Detailed Breakdown

All files located in: Assets/GV/Scripts/Web3/

### Web3Manager.cs
Central singleton for all blockchain functionality. Handles wallet connection across all 5 auth methods. Manages wallet address and AVAX balance. Fires events for UI systems. Must initialize before NetworkManager.

Serialized Fields:
- chainId (ulong, default 43113 = Fuji)
- defaultAuthProvider (AuthProvider, default Google)
- reownProjectId (string, for external wallets)

Events:
- OnWalletConnected(string address)
- OnWalletDisconnected()
- OnBalanceUpdated(string formatted)
- OnError(string message)

Key Methods:
- ConnectWithEmail(string email): validates email, creates InAppWallet
- ConnectWithSocial(AuthProvider): Google or Discord OAuth
- ConnectAsGuest(): one-tap guest wallet
- ConnectWithExternalWallet(): Reown/WalletConnect QR code or app popup
- Disconnect(): clears all wallet state
- RefreshBalance(): fetches AVAX balance from chain
- FetchBalance(): converts wei to ETH display format

All connect methods are async void, internally calling async Task ConnectWallet() which handles InAppWallet creation, address retrieval, balance fetch, and event firing.

### Web3Bootstrap.cs
Bootstrap script ensuring Web3 systems are ready before anything else. On dedicated server: instantiates RoomManager, loads gameplay scene, skips menu. On client: loads menu scene.

Serialized Fields:
- nextSceneName (string): scene to load on client
- gameplaySceneName (string): scene for dedicated server
- autoLoadNextScene (bool, default true)
- networkManagerPrefab (GameObject)
- roomManagerPrefab (GameObject)

Server detection: checks UNITY_SERVER define and command-line args (-server or --server).

### BattleRewardManager.cs
Singleton that survives scene changes. Manages ERC-20 token reward minting using Nethereum. Uses the reward wallet private key (set via Inspector, never committed to git).

Serialized Fields:
- tokenContractAddress (default: 0xBF7c298C0f3E4745Ec902ED0008223747EEbd0d1)
- tokenDisplayName (default: "PRANA")
- tokenSymbol (default: "PRANA")
- tokenDecimals (default: 18)
- rewardWalletPrivateKey (empty string, set via Inspector)
- rpcUrl (default: https://api.avax-test.network/ext/bc/C/rpc)
- rewardsByPlacement (List of PlacementReward, 4 entries)

Constants:
- MINT_TO_ABI: JSON ABI for mintTo(address, uint256)

Key Methods:
- DistributeRewards(List of PlayerRewardInfo): main async method. Creates fresh Web3 instance, calls mintTo for each player, polls confirmation 30 times at 1 second intervals, fires OnRewardsDistributed
- FetchTokenBalance(): reads player's PRANA balance via Thirdweb SDK balanceOf
- CreateFreshRewardWeb3(): creates new Nethereum Web3+Account per transaction to avoid stale nonce
- ToWei(float): converts token amount to wei using 10^18
- TestDistributeRewards(): debug method, mints 1st place rewards to connected wallet

Events:
- OnRewardsDistributed(List of RewardResult)
- OnRewardMinted(string address, float amount)
- OnTokenBalanceUpdated(string formatted)
- OnRewardError(string message)

Data Classes:
- PlayerRewardInfo: walletAddress (string), placement (int)
- RewardResult: walletAddress, placement, tokenAmount, success, message
- PlacementReward: placementLabel, tokenAmount, xpAmount, energyAmount, gemsAmount, coinsAmount

### BattleRewardBridge.cs
Bridges multiplayer game state to the reward system. Listens for match results from EliminationTracker. Detects local ship destruction. Distributes token rewards based on placement. Shows PostMatchRewardUI popup. Implements a 5-second fallback timeout if EliminationTracker doesn't respond.

Serialized Fields:
- postMatchUI (PostMatchRewardUI)
- enableTestKey (bool, default true)
- testKey (KeyCode, default R)
- eliminationTimeout (float, default 5)

Event Subscriptions:
- NetworkedGameManager.OnRaceFinished -> HandleRaceFinished
- EliminationTracker.OnMatchResultsReady -> HandleMatchResults
- Damageable.onDestroyed -> HandleLocalShipDestroyed (per ship)

Key Flow:
1. HandleMatchResults receives real placements from EliminationTracker
2. Extracts local player's placement
3. Checks wallet connection via Web3Manager
4. Calls BattleRewardManager.DistributeRewards
5. Shows PostMatchRewardUI with placement and rewards
6. Fallback: if EliminationTracker times out, calculates placement from alive/dead ship count

### ShipNFTManager.cs
Singleton managing ship NFT ownership. Queries ERC-1155 contract for balances. Supports 2-slot selection: Primary and Secondary ships must have different meshRootIndex (different ship types).

Contract: 0x8405209745b8f1A43D21876120543d20e4a7600C

Serialized Fields:
- contractAddress (string)
- ships (List of ShipDefinition)

Ship Types (by meshRootIndex):
- 0 = Aircraft A (Spaceship)
- 1 = Aircraft B (Vimana)

ShipDefinition fields: displayName, description, tokenId (int, NFT token ID), rarity (Common/Uncommon/Rare/Legendary), meshRootIndex (0 or 1), isDefault (bool), isLocked (bool), icon (Sprite)

Key Methods:
- FetchShipBalances(): async, queries balanceOf for each non-default ship via ThirdwebContract.Read
- SelectShipForSlot(int slot, ShipDefinition ship): validates ownership, lock status, and meshRootIndex conflict
- OwnsShip(ShipDefinition): default ships always return true

Events:
- OnShipsFetched(List of ShipDefinition)
- OnShipsConfirmed(ShipDefinition[])
- OnFetchError(string)

### CharacterNFTManager.cs
Singleton managing character NFT ownership. Queries same ERC-1155 contract. 4-slot selection system: 2 characters per ship roster. Auto-selects defaults into slots 0 and 2.

8 Characters total across 2 rosters:
- Roster A (shipRosterIndex 0, Spaceship): 4 characters
- Roster B (shipRosterIndex 1, Vimana): 4 characters

CharacterDefinition fields: displayName, description, tokenId, rarity (Common/Rare/Epic/Legendary), shipRosterIndex (0 or 1), isDefault (bool), isLocked (bool), icon (Sprite), characterStats (ScriptableObject)

Rarity Multipliers (applied to base stats):
- Common: 1.00x
- Rare: 1.06x
- Epic: 1.12x
- Legendary: 1.18x

Character Names: Aaryaveer, Ishvaya, Kaevik, Nysera, Roudra, Virexa, Vyanika, Zorvan

Key Methods:
- FetchCharacterBalances(): async, queries contract for each non-default character
- SelectCharacterForSlot(int slot, CharacterDefinition): validates slot (0-3), lock, ownership
- GetCharactersForShip(int shipRosterIndex): filters by roster
- ConfirmCharacters(): validates all 4 slots filled, fires OnCharactersConfirmed

### ShipSelectionUI.cs
Ship selection screen shown after wallet connection. Click-order selection: 1st click = Primary, 2nd click = Secondary. Ships must be different types (different meshRootIndex). Confirm button enables when both slots filled. Shows lore popup on icon click.

### CharacterSelectionUI.cs
Character selection screen shown after ship selection. Displays characters from both rosters in separate rows. Click-order selection: 1st click = Primary, 2nd click = Secondary per roster. Requires 4 total selections. Shows lore popup on icon click.

### ShipCardUI.cs and CharacterCardUI.cs
Reusable card components for selection screens. Display name, rarity, icon. Show lock overlay if not owned. Show selection highlight when picked. Show slot badge text (Primary/Secondary).

### WalletConnectPanel.cs
Wallet connection UI panel. Shows login options: email, Google, Discord, guest, external wallet. Orchestrates the full flow: Wallet Connect -> Ship Selection -> Character Selection -> Room Lobby -> Gameplay.

Serialized Fields:
- All button references (email, Google, Discord, guest, external)
- emailInputField (TMP_InputField)
- showAfterConnect (ship selection panel)
- characterSelectionPanel
- roomLobbyPanel
- gameplaySceneName (default: "MP_Mechanics_6")

### WalletHUD.cs
In-game HUD displaying wallet address, AVAX balance, and PRANA token balance. Auto-creates token balance text if not assigned. Refreshes balances every 30 seconds.

### PostMatchRewardUI.cs
Post-match popup showing placement (1st/2nd/3rd/4th), token reward earned, bonus rewards (XP/Energy/Gems/Coins), minting status, and total balance. Auto-creates UI if no panel assigned. Uses gold color scheme (#e6b818). Panel size 500x470 pixels.

---

## Event-Driven Architecture

The Web3 layer is fully decoupled from gameplay via events:

Web3Manager fires:
- OnWalletConnected -> WalletHUD updates, ShipNFTManager fetches ships, CharacterNFTManager fetches characters, BattleRewardManager fetches balance
- OnWalletDisconnected -> all systems reset
- OnBalanceUpdated -> WalletHUD updates AVAX display
- OnError -> WalletConnectPanel shows error

BattleRewardManager fires:
- OnRewardsDistributed -> PostMatchRewardUI shows results
- OnRewardMinted -> per-mint notification
- OnTokenBalanceUpdated -> WalletHUD updates PRANA display
- OnRewardError -> PostMatchRewardUI shows error

ShipNFTManager fires:
- OnShipsFetched -> ShipSelectionUI populates cards
- OnShipsConfirmed -> WalletConnectPanel transitions to character selection

CharacterNFTManager fires:
- OnCharactersFetched -> CharacterSelectionUI populates cards
- OnCharactersConfirmed -> WalletConnectPanel transitions to room lobby

All blockchain calls use async/await. None block the game loop.

---

## Scripting Define Symbols (Web3 related)

- THIRDWEB_REOWN: enables Reown AppKit integration in Thirdweb SDK
- Present on Standalone platform build

## IL2CPP Linker Preservation

The project preserves all types from: Thirdweb.dll, Nethereum.Web3, Nethereum.Contracts, Nethereum.Accounts, Nethereum.Signer, Nethereum.Signer.EIP712, Nethereum.HdWallet, Nethereum.KeyStore, Nethereum.JsonRpc, Nethereum.RLP, Nethereum.ABI, Nethereum.Util, Newtonsoft.Json, Portable.BouncyCastle, ADRaffy.ENSNormalize
