using UnityEngine;
using Fusion;
using System.Linq;

namespace GV.Network
{
    /// <summary>
    /// Simple game state manager for the prototype.
    /// Controls race countdown and basic game flow.
    /// </summary>
    public class NetworkedGameManager : NetworkBehaviour
    {
        public static NetworkedGameManager Instance { get; private set; }
        
        public enum GameState
        {
            WaitingForPlayers,
            Countdown,
            Racing,
            Finished
        }
        
        [Header("Settings")]
        [SerializeField] private int countdownSeconds = 3;
        [SerializeField] private int minPlayersToStart = 1;
        
        [Header("Debug")]
        [SerializeField] private bool autoStartRace = true;
        
        // Networked state
        [Networked] public GameState CurrentState { get; set; }
        [Networked] public TickTimer CountdownTimer { get; set; }
        [Networked] public TickTimer RaceTimer { get; set; }
        
        // Events
        public event System.Action OnCountdownStarted;
        public event System.Action<int> OnCountdownTick;
        public event System.Action OnRaceStarted;
        public event System.Action OnRaceFinished;
        
        private int _lastCountdownSecond = -1;
        
        private void Awake()
        {
            Instance = this;
        }
        
        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                CurrentState = GameState.WaitingForPlayers;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            
            switch (CurrentState)
            {
                case GameState.WaitingForPlayers:
                    UpdateWaiting();
                    break;
                    
                case GameState.Countdown:
                    UpdateCountdown();
                    break;
                    
                case GameState.Racing:
                    UpdateRacing();
                    break;
            }
        }
        
        private void UpdateWaiting()
        {
            if (!autoStartRace) return;
            
            int playerCount = Runner.ActivePlayers.Count();
            if (playerCount >= minPlayersToStart)
            {
                StartCountdown();
            }
        }
        
        private void UpdateCountdown()
        {
            if (CountdownTimer.Expired(Runner))
            {
                StartRace();
            }
        }
        
        private void UpdateRacing()
        {
            // Race logic - check for winner, etc.
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_StartCountdown()
        {
            OnCountdownStarted?.Invoke();
            _lastCountdownSecond = -1;
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_CountdownTick(int secondsRemaining)
        {
            OnCountdownTick?.Invoke(secondsRemaining);
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_RaceStarted()
        {
            OnRaceStarted?.Invoke();
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_RaceFinished()
        {
            OnRaceFinished?.Invoke();
        }
        
        public void StartCountdown()
        {
            if (!Object.HasStateAuthority) return;
            
            CurrentState = GameState.Countdown;
            CountdownTimer = TickTimer.CreateFromSeconds(Runner, countdownSeconds);
            RPC_StartCountdown();
            
            Debug.Log("[NetworkedGameManager] Countdown started");
        }
        
        public void StartRace()
        {
            if (!Object.HasStateAuthority) return;
            
            CurrentState = GameState.Racing;
            RaceTimer = TickTimer.CreateFromSeconds(Runner, 0);
            RPC_RaceStarted();
            
            Debug.Log("[NetworkedGameManager] Race started!");
        }
        
        public void EndRace()
        {
            if (!Object.HasStateAuthority) return;
            
            CurrentState = GameState.Finished;
            RPC_RaceFinished();
            
            Debug.Log("[NetworkedGameManager] Race finished!");
        }
        
        public override void Render()
        {
            if (CurrentState == GameState.Countdown && CountdownTimer.IsRunning)
            {
                float remaining = CountdownTimer.RemainingTime(Runner) ?? 0f;
                int currentSecond = Mathf.CeilToInt(remaining);
                
                if (currentSecond != _lastCountdownSecond && currentSecond > 0)
                {
                    _lastCountdownSecond = currentSecond;
                    OnCountdownTick?.Invoke(currentSecond);
                }
            }
        }
        
        public float GetRaceTime()
        {
            if (CurrentState != GameState.Racing) return 0f;
            float elapsed = RaceTimer.RemainingTime(Runner) ?? 0f;
            return -elapsed;
        }
        
        private void OnGUI()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected) return;
            
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 100, 10, 200, 80));
            GUILayout.BeginVertical("box");
            
            switch (CurrentState)
            {
                case GameState.WaitingForPlayers:
                    GUILayout.Label("Waiting for players...");
                    GUILayout.Label($"Players: {Runner.ActivePlayers.Count()}/{minPlayersToStart}");
                    break;
                    
                case GameState.Countdown:
                    float remaining = CountdownTimer.RemainingTime(Runner) ?? 0f;
                    GUILayout.Label($"Starting in: {Mathf.CeilToInt(remaining)}", 
                        new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter });
                    break;
                    
                case GameState.Racing:
                    GUILayout.Label("RACE!", 
                        new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter });
                    break;
                    
                case GameState.Finished:
                    GUILayout.Label("FINISHED!", 
                        new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter });
                    break;
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
