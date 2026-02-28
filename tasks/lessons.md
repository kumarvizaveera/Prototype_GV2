# GV2 — Lessons Learned

## Session: Feb 27, 2026

### Architecture Decisions
- **SCK Menu System over custom**: The Menus_InputSystem_SCK prefab provides a complete menu framework (Menu, MenuGroup, GameStateManager, SceneLoader). No need to build from scratch.
- **GameState is a ScriptableObject**: Created via Unity's Asset menu (VSX > Game State). The GameStateManager references these as assets in the Inspector.
- **GameStateManager is a singleton**: Only one should exist. It handles cursor, time scale, and state transitions.

### Key Patterns Found
- NetworkManager uses DontDestroyOnLoad and singleton pattern — Web3Manager should do the same
- Thirdweb wallet connection is async (await ConnectWallet) — must not block Fusion tick
- ThirdwebManager.Instance.ActiveWallet is null until connected — always check before using
- WalletOptions(provider: WalletProvider.InAppWallet, chainId, InAppWalletOptions) is the pattern for email/social login

### Things to Watch
- Web3 calls are async — never call them from Fusion's FixedUpdateNetwork or OnInput
- ThirdwebManager must be in the FIRST scene that loads and persist via DontDestroyOnLoad
- chain ID 43113 (Fuji) — NEVER use mainnet

### Issues Hit & Solved
- **Thirdweb 401 "Mainnets not enabled" error**: Even on Fuji testnet, Thirdweb's bundler/paymaster API requires the Starter billing plan ($5/mo). Free tier blocks all wallet connection calls. The fix was enabling billing at thirdweb.com/dashboard/settings/billing — no code changes needed.
- **Scene not loading after wallet connect**: WalletConnectPanel originally only hid itself after connecting — it didn't load the next scene. Fixed by adding a Play button + LoadGameplayScene() method using SceneManager.LoadScene(). Also added autoLoadAfterConnect option for quick testing.
- **Google Version Handler popup on import**: Thirdweb triggers a Google dependency cleanup dialog. Safe to click Apply — it removes obsolete ExternalDependencyManager DLLs.
- **BundleId confusion**: ThirdwebManager shows a BundleId field but it auto-fills from Application.identifier. Leave it blank.

### Phase 2 — Ship NFTs
- **Thirdweb server wallet EIP-7702 on Fuji**: The Thirdweb MCP server wallet (0x2bBc...) uses EIP-7702 transaction type. Fuji's bundler doesn't support EIP-7702 yet, so ALL transactions (deploy, mint, write) fail with "Chain does not support EIP-7702". Workaround: deploy and mint via the Thirdweb web dashboard instead.
- **Ship names don't need to be on-chain**: ERC-1155 tokens are just IDs + quantities. All display info (names, rarity, descriptions) lives in Unity Inspector config. This means names can be changed anytime without touching the blockchain.
- **ShipDefinition as Serializable class, not ScriptableObject**: Keeps ship config inline in ShipNFTManager's Inspector list — simpler for Veera to manage than separate asset files.

### Phase 3 — Battle Rewards
- **EIP-7702 still blocks the server wallet on Fuji**: Same issue as Phase 2. The Thirdweb MCP server wallet (0x2bBc...) uses EIP-7702 transaction type which Fuji's bundler doesn't support. Affects ALL write operations (deploy, mint, transfer) through the MCP.
- **Workaround: Dedicated reward wallet (regular EOA)**: Generated a plain Ethereum wallet with a private key. This wallet sends standard transactions — no EIP-7702. Granted it Minter role on the ERC-20 contract. Uses `PrivateKeyWallet.Create()` in the Thirdweb SDK.
- **Thirdweb MCP `createToken` doesn't support Fuji (43113)**: Returns "Contract factory not found on chain: 43113". Must deploy ERC-20 via dashboard, same as ERC-1155 in Phase 2.
- **`ThirdwebContract.Write()` pattern**: `contract.Write(wallet, method, weiValue, parameters)` — wallet is the signer, method is the Solidity function signature as a string, weiValue is 0 for non-payable, parameters is object array.
- **Bridge pattern for namespace separation**: BattleRewardBridge.cs sits in GV.Web3 but listens to GV.Network events. This way NetworkedGameManager doesn't import any Web3 code, and BattleRewardManager doesn't import any Fusion code. Clean separation.
- **Token balance as BigInteger**: ERC-20 balances come back as BigInteger in wei (10^18). Must convert to/from human-readable amounts using decimal math to avoid overflow.
- **PrivateKeyWallet doesn't exist in Thirdweb Unity SDK v6.1.3**: Only `PrivateKeyAccount` exists in the DLL. Do NOT use `PrivateKeyWallet` — it won't compile. Use **Nethereum** (`Nethereum.Web3.Accounts.Account`) instead for creating wallets from private keys and sending transactions. Nethereum is already installed via the Reown package (com.nethereum.unity v5.0.0).
- **Hybrid approach works great**: Use Nethereum for writing transactions (minting) from private-key wallets, and Thirdweb SDK for reading contract data (balanceOf). Each library handles what it's best at.
- **End-to-end reward flow confirmed working**: Press R → BattleRewardBridge triggers → BattleRewardManager mints via Nethereum → tx confirms on Fuji → 100 PRANA minted successfully.
