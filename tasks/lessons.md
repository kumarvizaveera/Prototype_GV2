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
