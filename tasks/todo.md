# GV2 — Current Tasks

## Phase 1: Web3 Foundation (Wallet Connect + Balance)

### In Progress
- [ ] Create Web3Manager.cs singleton (GV.Web3 namespace)
- [ ] Create WalletConnectPanel.cs (UI for email/social login)
- [ ] Create WalletHUD.cs (shows wallet address + AVAX balance)
- [ ] Create Bootstrap scene setup (ThirdwebManager persistence)
- [ ] Create GameState ScriptableObjects for menu flow
- [ ] Write Unity-side setup instructions for Veera

### Done
- [x] Thirdweb SDK imported
- [x] Server wallet created and funded
- [x] Reviewed SCK menu system — using it as foundation
- [x] Reviewed NetworkManager.cs flow
- [x] Reviewed Thirdweb PlaygroundManager examples

### Notes
- SCK's GameStateManager + Menu system will be the foundation
- GameState is a ScriptableObject (create via VSX > Game State menu in Unity)
- Wallet connect must happen BEFORE Fusion session join
- All calls target Fuji testnet (chain 43113)
- Thirdweb InAppWallet for email/social login — no MetaMask needed
