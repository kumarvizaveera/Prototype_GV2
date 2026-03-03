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

## Session: Mar 1, 2026

### Fusion 2 Lessons (EliminationTracker debugging)
- **`[Networked]` properties can ONLY be reliably read in `FixedUpdateNetwork()`**: Reading them in Unity's `Update()` returns stale/default values. If you need the value in Update, copy it to a plain bool/int inside FixedUpdateNetwork first.
- **`FixedUpdateNetwork()` may NOT be called on late-added NetworkBehaviours**: If a NetworkBehaviour is added to a scene object after the NetworkObject was already baked, Fusion may call `Spawned()` but NOT `FixedUpdateNetwork()`. Workaround: use C# events or Unity's `Update()` instead.
- **RPCs fire immediately on the host**: When host sends an RPC with `RpcTargets.All`, the handler executes on the host in the SAME frame. This can cause `InvalidOperationException: Collection was modified` if you iterate a list and an RPC modifies it. Fix: copy the list before iterating (`new List<T>(original)`).
- **Event subscription timing matters**: If you subscribe to an event in `Update()` (waiting for a manager to exist), the event may have already fired. Always check if the condition is already true after subscribing (e.g. check if ships already exist after subscribing to OnRaceStarted).

### Nethereum / Minting Lessons
- **Don't cache the Nethereum Web3 instance**: Nethereum's internal nonce tracker gets stale between sessions, causing "replacement transaction underpriced" errors. Create a fresh `new Web3(account, rpcUrl)` for each mint batch.
- **Always set explicit gas price on Avalanche**: Default gas price from Nethereum can conflict with pending transactions. Set `txInput.GasPrice = new HexBigInteger(30000000000)` (30 gwei) explicitly. On Fuji testnet this costs ~0.006 AVAX per mint.
- **Always fetch nonce with `BlockParameter.CreatePending()`**: Gets the next available nonce including pending transactions, preventing nonce collisions.
- **Keep the reward wallet funded**: Each mint costs ~0.006 AVAX at 30 gwei. Use the Fuji faucet (faucet.avax.network) to top up. 2 AVAX = ~300+ mints.

### Dedicated Server Migration (Part A)
- **Existing RPC input pipeline already works for dedicated server**: The `HasStateAuthority && !HasInputAuthority` path in FixedUpdateNetwork handles every ship on a dedicated server — no rewrites needed. Clients send input via RPC, server applies it. Perfect.
- **Guard pattern for dedicated server**: Use `if (NetworkManager.Instance != null && NetworkManager.Instance.IsDedicatedServer) return;` at the top of any rendering/UI method. Consistent, null-safe, and transparent in Host mode.
- **Three detection layers for dedicated server**: (1) `ServerMode` Inspector enum, (2) command-line `-server` flag, (3) `#if UNITY_SERVER` preprocessor define. The `Auto` mode checks all three.
- **On dedicated server, Runner.LocalPlayer is invalid**: There is no "local player" — all players are remote. Any code that compares `player == runner.LocalPlayer` must first check `IsDedicatedServer`.
- **Camera.main and FindFirstObjectByType<VehicleCamera>() crash on headless**: These return null on a headless server. Always bail early if dedicated server before touching cameras.
- **BattleRewardBridge self-disables on server**: Rather than guarding every method, the `OnEnable()` sets `enabled = false` and returns. Clean and complete.
- **Host mode is completely unaffected**: `IsDedicatedServer` defaults to `false`, so all guards are transparent. Zero behavior change for existing workflow.
- **ParrelSync clones inherit Inspector settings**: When you set ServerMode to DedicatedServer in the Inspector, the clone sees it too (they share scene data). The clone then tries to start as a second server, causing "ServerAlreadyInRoom." Fix: detect clones with `ParrelSync.ClonesManager.IsClone()` and force them to Client mode.
- **Runner can be null after failed StartGame**: If `StartGame()` fails, Fusion shuts down and destroys the Runner. Any code after `await StartGame()` that touches `Runner` will throw NullReferenceException. Always null-check Runner before using it post-StartGame.

### Auto-Creation Pattern for Scene Independence
- **BattleRewardBridge auto-creates BattleRewardManager**: When launching directly into gameplay scene (skipping Bootstrap), BattleRewardBridge creates a BattleRewardManager with default config. Set default private key in the serialized field so auto-created instances work.
- **PostMatchRewardUI retry subscription**: BattleRewardManager may be auto-created AFTER PostMatchRewardUI.OnEnable(). Use a `_subscribedToRewards` bool and retry in Update() until the manager exists.
- **Graceful no-wallet handling**: When Web3Manager doesn't exist (skipped Bootstrap), show "Connect wallet to receive rewards" instead of being stuck on "Minting tokens...".

### Double Audio in Multiplayer (Bug Fix — Mar 1)
- **Root Cause 1 — Pickup/power-up sounds**: OnTriggerEnter fires on EVERY machine that detects the collision. AudioSource.PlayClipAtPoint() and .Play() had no network authority check, so both host and client played the sound. Fix: use `NetworkAudioHelper.IsLocalPlayer()` to check `HasInputAuthority` before playing audio.
- **Root Cause 2 — Engine/weapon/boost sounds on remote ships**: DisableLocalInput() disabled input scripts but never touched AudioSources. EngineAudioController, Projectile, MissileWarningReceiver, BeamSoundEffectsController all kept playing on remote ships. Fix: mute all AudioSources and disable AudioListeners on remote ships in DisableLocalInput().
- **Key pattern**: For pickup scripts (MonoBehaviour, not NetworkBehaviour), find the NetworkObject on the collider's root and check `HasInputAuthority`. If no NetworkObject exists (offline testing), return true so audio still works.
- **EngineAudioController.OnEnable() re-starts audio**: After mesh swaps, audio roots toggle on/off. OnEnable calls m_Audio.Play(). Since we set `mute = true` AND `volume = 0f`, this is harmless — the audio "plays" silently.
- **Use mute + volume=0 instead of Destroy**: SCK scripts hold references to their AudioSources. Destroying them would cause NullRef. Muting is safer.

### Violent Rotation Snap During Roll (Bug Fix — Mar 1)
- **Root Cause**: In Client Authority mode, the client sends its rotation to the host via RPC. The host writes it to SyncRotation. The client reads SyncRotation back in ApplyServerCorrection() and compares it to its current rotation. During fast rolls, the client's current rotation is 90°+ ahead of the stale SyncRotation (which is just its own rotation from ~RTT ago). The snap triggers, resets angular velocity to zero, input re-applies roll, gap builds again → infinite violent oscillation.
- **Key Insight**: In Client Authority mode, SyncRotation IS the client's own data echoed back. Correcting toward your own stale data is always wrong. It's like trying to follow your own shadow — the faster you move, the worse it gets.
- **Fix**: Disabled ApplyServerCorrection() entirely. Client IS the authority — no correction needed. Position correction was already disabled for the same reason.
- **If respawn snap is needed later**: Use a flag-based approach (`_needsRotationSnap = true` set by respawn event) instead of continuous comparison against stale data.

### Proxy Position Sync Broken in Dedicated Server Mode (Bug Fix — Mar 1)
- **Root Cause**: NetworkTransform was only disabled on non-authority ships (clients). On the dedicated server (which has StateAuthority on ALL ships), NetworkTransform stayed ENABLED. It reads the ROOT transform every tick — but in SCK, physics moves the CHILD (Rigidbody), not the root. So NetworkTransform kept writing "spawn position" to Fusion's internal state. This conflicted with our custom SyncPosition [Networked] property that correctly held the RPC-reported position. Fusion's state replication got confused by the two competing position sources.
- **Why it worked before**: In Host mode (one player hosts), the host was also a player. NetworkTransform being enabled on the host was less visible because the host's own ship position came from local physics. In Dedicated Server mode with multiple clients and NO local player, every ship was remote to the server, maximizing the conflict.
- **Fix**: Disable NetworkTransform on ALL machines (server, host, AND clients) immediately in Spawned(). Our custom [Networked] SyncPosition/SyncRotation properties are the single source of truth.
- **Lesson**: When using custom [Networked] properties for position sync, ALWAYS disable NetworkTransform on the same object — on every machine, not just clients. Fusion's NetworkTransform and manual [Networked] position writes can interfere even though they're technically separate state values.

## Session: Mar 3, 2026

### Scene Flow & UI Lessons
- **Unity serialization overrides code defaults**: Changing `string gameplaySceneName = "SomeScene"` in code does NOT update the value already saved in the scene file. The Inspector keeps the old serialized value. To fix: set defaults to `""` and always set values in Inspector, or right-click the component → Reset.
- **Build Target "Dedicated Server" defines `UNITY_SERVER` globally**: Even in the Editor, this makes `IsDedicatedServer()` return true. If your server detection is misfiring, check your Build Target (File → Build Settings). Switch to "Windows, Mac, Linux" for normal Editor testing.
- **Hidden UI panels block raycasts**: When `Show(false)` hides a panel by disabling child elements, the panel's own `Image` component with `raycastTarget = true` still receives clicks. Fix: set `raycastTarget = false` on the panel Image when hiding.
- **Don't spawn players in menu scenes**: Fusion's `OnPlayerJoined()` fires as soon as someone connects — even if you're still in the menu scene. Use a flag (`_inGameplayScene`) and a pending list (`_pendingSpawns`) to delay spawning until the gameplay scene loads.
- **Two NetworkManagers = bad**: Photon Fusion only supports one Runner. Creating a separate "UI-only" NetworkManager would conflict. Keep one NetworkManager with all lobby UI references.
- **Inspector-only scene names prevent stale hardcodes**: When testing different scenes frequently, use empty string defaults and set scene names only in the Inspector. Hardcoded defaults get baked into serialized scene data and persist even after code changes.
- **Prefab asset references vs scene instance references**: The Network Manager prefab had `spawnSpline` pointing to the **Spline_2 prefab asset** (transform at origin), not the scene instance (correct world position). Prefab asset references survive scene loads and look non-null, but give wrong world-space positions. Fix: on gameplay scene load, ALWAYS use `GameObject.Find()` to grab the scene instance by name — never trust the serialized prefab reference for world-space data. Also clear cached spawn positions on every scene change.
