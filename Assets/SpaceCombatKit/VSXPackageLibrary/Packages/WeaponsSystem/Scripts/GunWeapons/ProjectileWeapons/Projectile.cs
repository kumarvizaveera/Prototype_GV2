using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.Pooling;
using UnityEngine.Events;
using VSX.Vehicles;
using VSX.Utilities;
using VSX.Health;
using Fusion;

namespace VSX.Weapons
{
    [System.Serializable]
    public class HitEffectsForSurfaceType
    {
        public List<GameObject> hitEffects = new List<GameObject>();
        public SurfaceType surfaceType;
    }

    /// <summary>
    /// Base class for a projectile.
    /// </summary>
    public class Projectile : NetworkBehaviour
    {

        [Header("Damage/Healing")]

        [SerializeField]
        protected HealthModifier healthModifier;
        public HealthModifier HealthModifier { get { return healthModifier; } }

        [Tooltip("The amount of damage/healing as a function of distance covered / Max Distance")]
        [SerializeField]
        protected AnimationCurve healthEffectByDistanceCurve = AnimationCurve.Linear(0, 1, 1, 1);

        [Header("Hit Effects")]

        [SerializeField]
        protected float hitEffectScaleMultiplier = 1;

        [Tooltip("The default hit effects for this projectile.")]
        [SerializeField]
        protected List<GameObject> defaultHitEffectPrefabs = new List<GameObject>();

        [Tooltip("Whether to spawn the hit effects when the projectile detonates (does not hit anything, just explodes).")]
        [SerializeField]
        protected bool spawnDefaultHitEffectsOnDetonation = true;

        [Tooltip("The hit effects to spawn when this projectile collides with specific surface types.")]
        [SerializeField]
        protected List<HitEffectsForSurfaceType> hitEffectOverrides = new List<HitEffectsForSurfaceType>();

        [Header("Area Effect Parameters")]

        [SerializeField]
        protected bool areaEffect = false;

        [SerializeField]
        protected float areaEffectRadius = 50;

        [SerializeField]
        protected AnimationCurve areaEffectFalloff = AnimationCurve.Linear(0, 1, 1, 0);

        [SerializeField]
        protected bool ignoreTriggerColliders = true;

        [SerializeField]
        protected LayerMask areaEffectLayerMask = ~0;

        [SerializeField]
        protected bool checkLineOfSight = true;


        [Header("Settings")]

        [SerializeField]
        protected CollisionScanner collisionScanner;

        [SerializeField]
        protected Detonator detonator;

        protected Transform senderRootTransform;

        [SerializeField]
        protected float speed = 100;

        public enum MovementUpdateMode
        {
            Update,
            FixedUpdate
        }

        [SerializeField]
        protected MovementUpdateMode movementUpdateMode = MovementUpdateMode.FixedUpdate;


        [Header("Disable After Lifetime")]

        [SerializeField]
        protected bool disableAfterLifetime = false;

        [SerializeField]
        protected float lifetime = 3;
        protected float lifeStartTime;


        [Header("Disable After Distance")]

        [SerializeField]
        protected bool disableAfterDistanceCovered = true;
        
        [Networked]
        protected Vector3 NetworkedLastPosition { get; set; }
        
        private Vector3 _localLastPosition;
        protected Vector3 lastPosition { 
            get { return (Object != null && Object.IsValid) ? NetworkedLastPosition : _localLastPosition; }
            set { 
                _localLastPosition = value;
                if (Object != null && Object.IsValid && Object.HasStateAuthority) NetworkedLastPosition = value;
            }
        }
        
        [Networked]
        protected float NetworkedDistanceCovered { get; set; }

        private float _localDistanceCovered;
        protected float distanceCovered { 
             get { return (Object != null && Object.IsValid) ? NetworkedDistanceCovered : _localDistanceCovered; }
            set { 
                _localDistanceCovered = value;
                if (Object != null && Object.IsValid && Object.HasStateAuthority) NetworkedDistanceCovered = value;
            }
        }

        [SerializeField]
        protected float maxDistance = 1000;

        protected List<TrailRenderer> trailRenderers = new List<TrailRenderer>();

        protected List<Renderer> renderers = new List<Renderer>();

        protected bool detonated = false;
        public bool Detonated { get { return detonated; } }

        public UnityEvent onDetonated;

        protected GameAgent owner;

        public UnityEvent onOwnedByPlayer;
        public UnityEvent onOwnedByAI;

        protected List<IGameAgentOwnable> gameAgentOwnables = new List<IGameAgentOwnable>();

        [Networked] public float NetworkedSpeed { get; set; }
        [Networked] public float NetworkedMaxDistance { get; set; }
        [Networked] public float NetworkedDamageMultiplier { get; set; }
        [Networked] public float NetworkedHealingMultiplier { get; set; }
        [Networked] public NetworkId OwnerId { get; set; }
        [Networked] public NetworkId NetworkedTargetId { get; set; }

        // Spawn position/rotation — set in onBeforeSpawned so proxies get the correct
        // initial position in the first snapshot. Without NetworkTransform, Fusion does NOT
        // replicate the position passed to runner.Spawn() to proxies.
        [Networked] public Vector3 NetworkedSpawnPosition { get; set; }
        [Networked] public Quaternion NetworkedSpawnRotation { get; set; }

        /// <summary>
        /// Whether proxy simulation should use manual transform.Translate movement.
        /// Override to return false for projectiles that use Rigidbody physics + engine steering (e.g. Missiles).
        /// </summary>
        protected virtual bool UseManualProxyMovement => true;

        public bool IsVisualDummy { get; private set; } = false;

        public void SetVisualDummy()
        {
            IsVisualDummy = true;
        }

        /// <summary>
        /// Returns true if this projectile should apply damage/healing.
        /// Networked projectiles: only on state authority (host).
        /// Non-networked projectiles (turret-spawned): only on host or single-player.
        /// Without this, turret projectiles apply damage on every machine, causing
        /// double damage since NetworkedHealthSync also syncs from host to clients.
        /// </summary>
        private static NetworkRunner _cachedRunner;
        private static float _lastRunnerLookup;

        protected bool IsDamageAuthority
        {
            get
            {
                if (IsVisualDummy) return false;
                // Networked projectile — standard Fusion authority check
                if (Object != null) return Object.HasStateAuthority;
                // Non-networked projectile (e.g. turret-spawned via Instantiate).
                // If Fusion is running, only the server/host should apply damage.
                // Cache the runner lookup to avoid FindObjectOfType every collision.
                if (_cachedRunner == null || Time.time - _lastRunnerLookup > 2f)
                {
                    _cachedRunner = FindFirstObjectByType<NetworkRunner>();
                    _lastRunnerLookup = Time.time;
                }
                if (_cachedRunner != null && _cachedRunner.IsRunning)
                    return _cachedRunner.IsServer;
                // Single-player / no Fusion — always apply damage
                return true;
            }
        }


        public override void Render()
        {
            if(Object != null && Object.IsValid)
            {
                 // Sync networked values to local modifier (for UI or other local scripts)
                 // Only needed if we are not the StateAuthority, but good to keep consistent.
                 if(!Object.HasStateAuthority)
                 {
                     healthModifier.DamageMultiplier = NetworkedDamageMultiplier;
                     healthModifier.HealingMultiplier = NetworkedHealingMultiplier;

                     // If NetworkedSpeed arrived late, sync it to the local field
                     if (NetworkedSpeed > 0 && speed != NetworkedSpeed)
                     {
                         speed = NetworkedSpeed;
                     }

                     // Use the best available speed: networked → prefab fallback
                     float renderSpeed = Speed; // Speed getter already falls back to local 'speed'

                     // PROXY SIMULATION: Simulate movement visually for clients!
                     // Server spawns the object, but Fusion doesn't run FixedUpdateNetwork for Proxies.
                     // Without a NetworkTransform, we must manually extrapolate movement.
                     //
                     // NOTE: Skip manual translate for physics-based projectiles (e.g. Missiles) that
                     // use Rigidbody + engine steering. Those projectiles already move via Missile.Update()
                     // which runs on all instances. Manual translate would cause double movement, making
                     // missiles overshoot their target on the client screen.
                     if (UseManualProxyMovement && (movementUpdateMode == MovementUpdateMode.Update || movementUpdateMode == MovementUpdateMode.FixedUpdate))
                     {
                          if (detonated) return; // Prevent movement/collision if we already hit something locally

                          float moveDistance = renderSpeed * Time.deltaTime;
                          Vector3 previousPosition = transform.position;
                          Vector3 direction = Vector3.forward; // Local forward

                          // Convert local forward to world space direction for translation and raycast
                          Vector3 worldDirection = transform.TransformDirection(direction);

                          // Check collision locally before moving.
                          // Add a lag-compensation look-ahead (e.g., 0.15s of travel) to ensure proxy hits before Host despawns it.
                          float lookAheadDistance = moveDistance + (renderSpeed * 0.15f);
                          if (Physics.Raycast(previousPosition, worldDirection, out RaycastHit physHit, lookAheadDistance, collisionScanner != null ? collisionScanner.HitMask : Physics.DefaultRaycastLayers, ignoreTriggerColliders ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide))
                          {
                               // Filter hierarchy collision
                               if (senderRootTransform != null && (physHit.transform == senderRootTransform || physHit.transform.IsChildOf(senderRootTransform)))
                               {
                                   // Ignore self hit, continue moving
                                   transform.Translate(direction * moveDistance);
                               }
                               else
                               {
                                   // Visual Hit detected!
                                   transform.position = physHit.point; // Snap to hit position
                                   OnCollision(physHit);               // Trigger local visual effects and damage events

                                   // Hide visual renderers immediately so it doesn't pass through
                                   foreach (Renderer rend in renderers)
                                   {
                                       rend.enabled = false;
                                   }

                                   detonated = true; // Mark detonated locally so it stops moving
                               }
                          }
                          else
                          {
                              transform.Translate(direction * moveDistance);
                          }
                     }
                     distanceCovered += (renderSpeed * Time.deltaTime);
                 }
            }
        }

        protected virtual void Reset()
        {
            collisionScanner = transform.GetComponent<CollisionScanner>();
            if (collisionScanner == null)
            {
                collisionScanner = gameObject.AddComponent<CollisionScanner>();
            }

            detonator = transform.GetComponent<Detonator>();
            if (detonator == null)
            {
                detonator = gameObject.AddComponent<Detonator>();
            }
        }


        // Cached list of AudioSources that originally had playOnAwake enabled.
        // We disable them in Awake() (before they play) and selectively re-enable in Spawned().
        private List<AudioSource> _awakeAudioSources = new List<AudioSource>();

        protected virtual void Awake()
        {
            // CollisionScanner is not used for networked projectiles in the same way,
            // but we keep it for reference or if used in single player without Fusion instantiation?
            // Actually, if we are fully networking, we should rely on Fusion's LagCompensation.
            // For now, let's keep references but disable it if networked.
            if (collisionScanner != null) collisionScanner.onHitDetected.AddListener(OnCollision);

            trailRenderers = new List<TrailRenderer>(GetComponentsInChildren<TrailRenderer>(true));

            renderers = new List<Renderer>(GetComponentsInChildren<Renderer>(true));

            gameAgentOwnables = new List<IGameAgentOwnable>(transform.GetComponentsInChildren<IGameAgentOwnable>());

            // PRE-EMPTIVELY disable all AudioSources to prevent playOnAwake from firing
            // before Spawned() can check authority. Spawned() re-enables on state authority.
            // This prevents duplicate fire sounds: the client already plays audio locally via
            // onProjectileLaunched, so the replicated proxy projectile must NOT also play audio.
            AudioSource[] allAudio = GetComponentsInChildren<AudioSource>(true);
            foreach (AudioSource src in allAudio)
            {
                if (src.playOnAwake)
                {
                    _awakeAudioSources.Add(src);
                    src.playOnAwake = false;
                }
                src.Stop();
                src.enabled = false;
            }
        }

        public override void Spawned()
        {
            // --- PROXY: Apply spawn position/rotation from networked state ---
            // Without NetworkTransform, Fusion does NOT replicate the position passed to
            // runner.Spawn() to proxies. The proxy instantiates the prefab at (0,0,0).
            // We set NetworkedSpawnPosition/Rotation in onBeforeSpawned so it's in the first snapshot.
            if (!Object.HasStateAuthority)
            {
                if (NetworkedSpawnPosition != Vector3.zero)
                {
                    transform.position = NetworkedSpawnPosition;
                    transform.rotation = NetworkedSpawnRotation;
                }
                // Audio stays disabled on proxies (disabled in Awake).
                // The client already gets fire audio from the local onProjectileLaunched event.
            }

            lastPosition = transform.position;
            distanceCovered = 0;
            lifeStartTime = Time.time;
            detonated = false;

            // Re-enable renderers that might have been hidden by proxy local hit detection
            foreach (Renderer rend in renderers)
            {
                rend.enabled = true;
            }

            foreach (TrailRenderer trailRenderer in trailRenderers)
            {
                trailRenderer.Clear();
            }

            // Disable standard collision scanner as we use FixedUpdateNetwork
            if (collisionScanner != null)
            {
                collisionScanner.enabled = false;
            }

            // Try to resolve owner from ID
             if (Runner.TryFindObject(OwnerId, out NetworkObject ownerObj))
            {
                GameAgent agent = ownerObj.GetComponent<GameAgent>();
                if(agent != null) SetOwner(agent);
            }

            if (Object.HasStateAuthority)
            {
                 // Re-enable AudioSources on the authority (host) that were disabled in Awake().
                 // The host plays projectile audio via onProjectileLaunched in ProjectileWeaponUnit,
                 // but some projectiles may have flight/ambient sounds that should play on authority.
                 foreach (AudioSource src in _awakeAudioSources)
                 {
                     src.enabled = true;
                     src.playOnAwake = true;
                     src.Play();
                 }

                 // Only set defaults if onBeforeSpawned didn't already set them.
                 // onBeforeSpawned sets these BEFORE the first snapshot, guaranteeing proxies
                 // receive non-zero values immediately.
                 if (NetworkedSpeed == 0) NetworkedSpeed = speed;
                 if (NetworkedMaxDistance == 0) NetworkedMaxDistance = maxDistance;
                 if (NetworkedDamageMultiplier == 0) NetworkedDamageMultiplier = 1;
                 if (NetworkedHealingMultiplier == 0) NetworkedHealingMultiplier = 1;

                 // Sync the local field to match networked (in case onBeforeSpawned set a modified value)
                 speed = NetworkedSpeed;

            }
            else
            {
                // Proxy: Apply initial values from networked state.
                // These should already be set via onBeforeSpawned snapshot.
                healthModifier.DamageMultiplier = NetworkedDamageMultiplier;
                healthModifier.HealingMultiplier = NetworkedHealingMultiplier;
                speed = NetworkedSpeed > 0 ? NetworkedSpeed : speed; // Fallback to prefab speed
                // Hide this networked projectile if we are the client who fired it
                // because we already spawned a local visual dummy instantly!
                if (OwnerId.IsValid && Runner.TryFindObject(OwnerId, out NetworkObject owner) && owner.HasInputAuthority)
                {
                    foreach (Renderer r in renderers) r.enabled = false;
                    foreach (TrailRenderer t in trailRenderers) t.enabled = false;
                }
            }
        }

        /// <summary>
        /// Set the owner of this projectile.
        /// </summary>
        /// <param name="owner">The owner.</param>
        public virtual void SetOwner(GameAgent owner)
        {
            this.owner = owner;

            if (owner != null)
            {
                if (owner.IsPlayer)
                {
                    OnOwnedByPlayer();
                }
                else
                {
                    OnOwnedByAI();
                }
                
                // If we are authority, sync the owner ID
                if (Object != null && Object.HasStateAuthority)
                {
                    var no = owner.GetComponent<NetworkObject>();
                    if (no != null)
                    {
                        OwnerId = no.Id;
                    }
                }
            }

            for (int i = 0; i < gameAgentOwnables.Count; ++i)
            {
                gameAgentOwnables[i].Owner = owner;
            }
        }


        /// <summary>
        /// Called when the projectile is owned by the player.
        /// </summary>
        protected virtual void OnOwnedByPlayer()
        {
            onOwnedByPlayer.Invoke();
        }


        /// <summary>
        /// Called when the projectile is owned by an AI (non player).
        /// </summary>
        protected virtual void OnOwnedByAI()
        {
            onOwnedByAI.Invoke();
        }


        public virtual void SetRendererLayers(int layer)
        {
            foreach (Renderer rend in renderers)
            {
                rend.gameObject.layer = layer;
            }
        }


        protected virtual void OnEnable()
        {
            // Standard OnEnable is less useful for pooled NetworkObjects which use Spawned.
            // But if used as MonoBehaviour, keep this.
            if (Object == null)
            {
                lastPosition = transform.position;
                distanceCovered = 0;
                lifeStartTime = Time.time;

                foreach (TrailRenderer trailRenderer in trailRenderers)
                {
                    trailRenderer.Clear();
                }

                if (collisionScanner != null) collisionScanner.enabled = true;

                detonated = false;
            }
        }


        /// <summary>
        /// Set the damage multiplier for this projectile.
        /// </summary>
        /// <param name="damageMultiplier">The damage multiplier.</param>
        public virtual void SetDamageMultiplier(float damageMultiplier)
        {
            healthModifier.DamageMultiplier = damageMultiplier;
             if (Object != null && Object.HasStateAuthority)
            {
                NetworkedDamageMultiplier = damageMultiplier;
            }
        }


        /// <summary>
        /// Set the healing multiplier for this projectile.
        /// </summary>
        /// <param name="healingMultiplier">The healing multiplier.</param>
        public virtual void SetHealingMultiplier(float healingMultiplier)
        {
            healthModifier.HealingMultiplier = healingMultiplier;
             if (Object != null && Object.HasStateAuthority)
            {
                NetworkedHealingMultiplier = healingMultiplier;
            }
        }


        public virtual void Detonate()
        {
            // Area damage should ONLY run on the damage authority (host / single-player).
            // Proxies and client turret projectiles should not apply damage.
            // AreaEffect handles per-target authority checks internally,
            // allowing clients to damage non-networked targets (turrets).
            if (areaEffect) AreaEffect();

            if (detonator != null && detonator.DetonationState == DetonationState.Reset) detonator.Detonate();

            if (spawnDefaultHitEffectsOnDetonation)
            {
                for (int i = 0; i < defaultHitEffectPrefabs.Count; ++i)
                {
                    GameObject spawnedHitEffect;
                    if (PoolManager.Instance != null)
                    {
                        spawnedHitEffect = PoolManager.Instance.Get(defaultHitEffectPrefabs[i], transform.position, transform.rotation);
                    }
                    else
                    {
                        spawnedHitEffect = Instantiate(defaultHitEffectPrefabs[i], transform.position, transform.rotation);
                    }

                    spawnedHitEffect.transform.localScale = defaultHitEffectPrefabs[i].transform.localScale * hitEffectScaleMultiplier;
                }
            }

            if (collisionScanner != null) collisionScanner.enabled = false;

            detonated = true;

            onDetonated.Invoke();

            // On proxy detonation: hide the missile visually so it doesn't keep flying
            // while waiting for the host to despawn the NetworkObject.
            if (Object != null && !Object.HasStateAuthority)
            {
                foreach (Renderer rend in renderers)
                {
                    rend.enabled = false;
                }
                foreach (TrailRenderer trail in trailRenderers)
                {
                    trail.enabled = false;
                }
            }

            // Should despawn networked object
            if (Object != null && Object.HasStateAuthority)
            {
                Runner.Despawn(Object);
            }
            else if (Object == null) // Legacy/Local fallback
            {
                 gameObject.SetActive(false);
            }

        }

        /// <summary>
        /// Set the sender's root transform.
        /// </summary>
        /// <param name="senderRootTransform">The sender's root transform.</param>
        public virtual void SetSenderRootTransform(Transform senderRootTransform)
        {
            this.senderRootTransform = senderRootTransform;

            if (collisionScanner != null) collisionScanner.RootTransform = senderRootTransform;
        }


        public virtual float Damage(HealthType healthType)
        {
            return healthModifier.GetDamage(healthType);
        }


        public virtual float Healing(HealthType healthType)
        {
            return healthModifier.GetHealing(healthType);
        }


        public virtual void AddVelocity(Vector3 addedVelocity) { }


        public virtual float Speed
        {
            get { 
                if (Object != null && Object.IsValid) return NetworkedSpeed > 0 ? NetworkedSpeed : speed;
                return speed; 
            }
            set { 
                speed = value;
                if (Object != null && Object.HasStateAuthority) NetworkedSpeed = value;
            }
        }

        protected float SafeDistanceCovered
        {
            get {
                return distanceCovered;
            }
        }

         protected float SafeNetworkedMaxDistance
        {
            get {
                if(Object != null && Object.IsValid) { try { return NetworkedMaxDistance; } catch {} }
                return maxDistance;
            }
        }

        public virtual float Range
        {
            get { return Mathf.Min(disableAfterLifetime ? lifetime * Speed : Mathf.Infinity, disableAfterDistanceCovered ? (Object != null ? NetworkedMaxDistance : maxDistance) : Mathf.Infinity); }
        }

        public virtual void SetMaxDistance(float maxDistance)
        {
            this.maxDistance = maxDistance;
             if (Object != null && Object.HasStateAuthority) NetworkedMaxDistance = maxDistance;
        }

        public virtual void SetLifetime(float lifetime)
        {
            this.lifetime = lifetime;
        }

        protected virtual void OnCollision(RaycastHit hit)
        {
            transform.position = hit.point;

            DamageReceiver damageReceiver = null;

            bool isAuthority = IsDamageAuthority;

            if (!areaEffect)
            {
                damageReceiver = hit.collider.GetComponent<DamageReceiver>();

                if (damageReceiver != null)
                {
                    // Determine if this target's health is synced by NetworkedHealthSync.
                    // If it IS synced, only the host should apply damage (clients get updates via sync).
                    // If it is NOT synced (e.g. turrets under a scene NetworkObject like RaceNetwork),
                    // each machine must apply damage locally.
                    //
                    // We check for NetworkedHealthSync on the damageable's hierarchy rather than
                    // NetworkObject on root, because turrets can be children of a networked scene
                    // object (RaceNetwork) without having their own health sync.
                    bool hasNetworkedHealthSync = false;
                    if (!isAuthority)
                    {
                        // Walk up hierarchy looking for a NetworkedHealthSync component.
                        // We can't reference the GV assembly directly from VSX, so check
                        // by type name on NetworkBehaviour components in the hierarchy.
                        Transform checkTransform = damageReceiver.transform;
                        while (checkTransform != null)
                        {
                            foreach (var nb in checkTransform.GetComponents<NetworkBehaviour>())
                            {
                                if (nb.GetType().Name == "NetworkedHealthSync")
                                {
                                    hasNetworkedHealthSync = true;
                                    break;
                                }
                            }
                            if (hasNetworkedHealthSync) break;
                            checkTransform = checkTransform.parent;
                        }
                    }

                    // Apply damage if: we have authority (host), OR the target has no health sync (turret)
                    if (isAuthority || !hasNetworkedHealthSync)
                    {
                        HealthEffectInfo info = new HealthEffectInfo();
                        info.worldPosition = hit.point;
                        info.healthModifierType = healthModifier.HealthModifierType;
                        info.sourceRootTransform = senderRootTransform;

                        // Damage
                        info.amount = healthModifier.GetDamage(damageReceiver.HealthType) * healthEffectByDistanceCurve.Evaluate(SafeDistanceCovered / SafeNetworkedMaxDistance);

                        if (!Mathf.Approximately(info.amount, 0))
                        {
                            damageReceiver.Damage(info);
                        }

                        // Healing
                        info.amount = healthModifier.GetHealing(damageReceiver.HealthType) * healthEffectByDistanceCurve.Evaluate(SafeDistanceCovered / SafeNetworkedMaxDistance);

                        if (!Mathf.Approximately(info.amount, 0))
                        {
                            damageReceiver.Heal(info);
                        }
                    }
                }
            }

            if (damageReceiver != null)
            {
                CollisionHitEffects(hit, damageReceiver.SurfaceType);
            }
            else
            {
                CollisionHitEffects(hit, null);
            }

            if (detonator != null)
            {
                detonator.Detonate(hit);
            }

            Detonate();

        }


        protected virtual void CollisionHitEffects(RaycastHit hit, SurfaceType surfaceType)
        {
            int effectOverrideIndex = -1;
            if (surfaceType != null)
            {
                for (int i = 0; i < hitEffectOverrides.Count; ++i)
                {
                    if (hitEffectOverrides[i].surfaceType == surfaceType)
                    {
                        effectOverrideIndex = i;
                        break;
                    }
                }
            }

            if (effectOverrideIndex == -1)
            {              
                for (int i = 0; i < defaultHitEffectPrefabs.Count; ++i)
                {
                    GameObject spawnedHitEffect;
                    if (PoolManager.Instance != null)
                    {
                        spawnedHitEffect = PoolManager.Instance.Get(defaultHitEffectPrefabs[i], hit.point, Quaternion.LookRotation(hit.normal));
                    }
                    else
                    {
                        spawnedHitEffect = Instantiate(defaultHitEffectPrefabs[i], hit.point, Quaternion.LookRotation(hit.normal));
                    }

                    spawnedHitEffect.transform.localScale = defaultHitEffectPrefabs[i].transform.localScale * hitEffectScaleMultiplier;
                }
            }
            else
            {
                for (int i = 0; i < hitEffectOverrides[effectOverrideIndex].hitEffects.Count; ++i)
                {
                    GameObject spawnedHitEffect;
                    if (PoolManager.Instance != null)
                    {
                        spawnedHitEffect = PoolManager.Instance.Get(hitEffectOverrides[effectOverrideIndex].hitEffects[i], hit.point, Quaternion.LookRotation(hit.normal));
                    }
                    else
                    {
                        spawnedHitEffect = Instantiate(hitEffectOverrides[effectOverrideIndex].hitEffects[i], hit.point, Quaternion.LookRotation(hit.normal));
                    }

                    spawnedHitEffect.transform.localScale = hitEffectOverrides[effectOverrideIndex].hitEffects[i].transform.localScale * hitEffectScaleMultiplier;
                }
            }
        }


        protected virtual void AreaEffect()
        {
            if (!areaEffect) return;

            bool isAuthority = IsDamageAuthority;

            // If not authority, we can still damage non-networked targets (turrets).
            // We'll check per-target below.
            if (!isAuthority)
            {
                // Quick check: if Fusion isn't running at all, IsDamageAuthority would be true.
                // So if we're here, Fusion IS running and we're the client.
                // We'll still iterate to find non-networked targets.
            }

            if (Mathf.Approximately(areaEffectRadius, 0)) return;

            // Get colliders in range
            Collider[] colliders = Physics.OverlapSphere(transform.position, areaEffectRadius);

            // Track damageables already effected
            List<Damageable> hitDamageables = new List<Damageable>();

            for (int i = 0; i < colliders.Length; ++i)
            {
                // Ignore trigger colliders if that's checked
                if (ignoreTriggerColliders && colliders[i].isTrigger)
                {
                    continue;
                }

                // Check if the collider is on an area effect layer
                if (((1 << colliders[i].gameObject.layer) & areaEffectLayerMask) == 0)
                {
                    continue;
                }

                // Check line of sight if that's checked
                if (checkLineOfSight)
                {
                    RaycastHit hit;
                    Vector3 lineOfSightOrigin = transform.position - transform.forward * 0.01f;
                    if (Physics.Raycast(lineOfSightOrigin, (colliders[i].transform.position - lineOfSightOrigin).normalized, out hit, areaEffectRadius, areaEffectLayerMask,
                                        ignoreTriggerColliders ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide))
                    {
                        if (hit.collider != colliders[i]) continue;
                    }
                }

                DamageReceiver damageReceiver = colliders[i].GetComponent<DamageReceiver>();
                if (damageReceiver != null)
                {
                    // Skip targets whose health is synced via NetworkedHealthSync (host handles those).
                    // Targets WITHOUT NetworkedHealthSync (e.g. turrets parented under a scene NetworkObject)
                    // must be damaged locally on each machine.
                    if (!isAuthority)
                    {
                        bool hasSync = false;
                        Transform check = damageReceiver.transform;
                        while (check != null)
                        {
                            foreach (var nb in check.GetComponents<NetworkBehaviour>())
                            {
                                if (nb.GetType().Name == "NetworkedHealthSync")
                                {
                                    hasSync = true;
                                    break;
                                }
                            }
                            if (hasSync) break;
                            check = check.parent;
                        }
                        if (hasSync) continue; // Synced target — host will handle it
                    }

                    // If damageable not already effected
                    if (hitDamageables.IndexOf(damageReceiver.Damageable) == -1)
                    {
                        // Get closest point
                        Vector3 closestPoint = damageReceiver.GetClosestPoint(transform.position);

                        // Implement damage
                        float distanceFromSource = Vector3.Distance(transform.position, closestPoint);
                        if (distanceFromSource < areaEffectRadius)
                        {
                            HealthEffectInfo info = new HealthEffectInfo();
                            info.worldPosition = transform.position;
                            info.healthModifierType = healthModifier.HealthModifierType;
                            info.sourceRootTransform = senderRootTransform;

                            // Damage

                            info.amount = healthModifier.GetDamage(damageReceiver.HealthType) * healthEffectByDistanceCurve.Evaluate(SafeDistanceCovered / SafeNetworkedMaxDistance);
                            info.amount *= areaEffectFalloff.Evaluate(distanceFromSource / areaEffectRadius);

                            if (!Mathf.Approximately(info.amount, 0))
                            {
                                damageReceiver.Damage(info);
                            }

                            // Healing

                            info.amount = healthModifier.GetHealing(damageReceiver.HealthType) * healthEffectByDistanceCurve.Evaluate(SafeDistanceCovered / SafeNetworkedMaxDistance);

                            if (!Mathf.Approximately(info.amount, 0))
                            {
                                damageReceiver.Heal(info);
                            }
                        }

                        // Add to list of already effected damageables
                        hitDamageables.Add(damageReceiver.Damageable);
                    }
                    else
                    {
                        continue;
                    }
                }
            }
        }


        protected virtual void MovementUpdate()
        {
            if (detonator != null && (detonator.DetonationState == DetonationState.Detonating || detonator.DetonationState == DetonationState.Detonated)) return;
            
            float currentSpeed = speed;
            if (Object != null && Object.IsValid) { try { currentSpeed = NetworkedSpeed; } catch {} }
            if(currentSpeed == 0) currentSpeed = speed; // fallback

            float deltaTime = 0f;
             if (Object != null && Object.IsValid) 
             {
                 deltaTime = Runner.DeltaTime;
             }
             else
             {
                 deltaTime = (movementUpdateMode == MovementUpdateMode.Update ? Time.deltaTime : Time.fixedDeltaTime);
             }

            float moveDistance = currentSpeed * deltaTime;
            Vector3 previousPosition = transform.position;
            
            // Move
            transform.Translate(Vector3.forward * moveDistance);

            // Networked Collision Check
            if (Object != null && Object.IsValid)
            {
               // If strict lag compensation is essential, we would use Runner.LagCompensation.Raycast here.
               // For simple projectiles, a standard Physics Raycast on the StateAuthority (Host) is often sufficient 
               // combined with Client Prediction (which is what we are doing here by running this on all clients).
               // HOWEVER, for true accuracy, Host should validate hits. 
               
               // Let's implement basic Raycast from prev to new position.
               
               // Using LagCompensation for hit detection if available
               if (Object.HasStateAuthority)
               {
                   var lagComp = Runner.GetPhysicsScene(); // Standard physics scene for now unless full lag comp is set up
                    // Or better, use Runner.LagCompensation if configured. 
                    // To be safe and simple: Standard Physics.Raycast works if everyone agrees on positions roughly.
                    // But for fast projectiles, we need Raycast.
                   
                    Vector3 direction = (transform.position - previousPosition).normalized;
                    float dist = Vector3.Distance(previousPosition, transform.position);

                    if (Runner.LagCompensation != null && Runner.LagCompensation.Raycast(previousPosition, direction, dist, Object.InputAuthority, out var hit, collisionScanner != null ? collisionScanner.HitMask : Physics.DefaultRaycastLayers, HitOptions.IgnoreInputAuthority))
                    {
                        // We hit something!
                         // Check self collision if needed
                        if (hit.GameObject != null)
                        {
                            if(ignoreTriggerColliders && hit.Collider.isTrigger) return;
                             // Filter hierarchy collision
                            if (senderRootTransform != null && (hit.GameObject.transform == senderRootTransform || hit.GameObject.transform.IsChildOf(senderRootTransform)))
                            {
                                return;
                            }
                            
                            // Convert LagComp hit to RaycastHit compatible handling
                            // Simply invoke OnCollision with constructed RaycastHit or similar logic
                            // Since LagComp hit is different struct, we might need to adapt.
                            // For simplicity, let's use Physics.Raycast first which is easier to integrate with existing code
                            // provided the colliders are present.
                        }
                    }
                    
                    // Fallback to standard physics for now to match legacy behavior exactly but inside FUN
                     if (Physics.Raycast(previousPosition, direction, out RaycastHit physHit, dist, collisionScanner != null ? collisionScanner.HitMask : Physics.DefaultRaycastLayers, ignoreTriggerColliders ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide))
                     {
                           if (senderRootTransform != null && (physHit.transform == senderRootTransform || physHit.transform.IsChildOf(senderRootTransform))) return;

                           OnCollision(physHit);
                     }
               }
            }
        }
        
        public override void FixedUpdateNetwork()
        {
             MovementUpdate();
             
             if (Object == null || !Object.IsValid) return;

             if (Object.HasStateAuthority)
             {
                 try {
                     distanceCovered += (transform.position - lastPosition).magnitude;
                 } catch {}
                 lastPosition = transform.position;

                 float currentMaxDist = SafeNetworkedMaxDistance;
                 
                  if (disableAfterLifetime)
                {
                    // Use Runner.SimulationTime instead of Time.time? 
                    // Actually simply tracking lifetime is enough
                    if (Time.time - lifeStartTime > lifetime) // Time.time is generally synced to Runner.Time in Fusion 2 context? Better use Runner.Tick * DeltaTime
                    {
                         // Or just simple float timer
                    }
                }
                
                if (disableAfterDistanceCovered)
                {
                   if (SafeDistanceCovered >= currentMaxDist)
                   {
                      Detonate(); // Will Despawn
                   }
                }
             }
        }


        protected virtual void DisableProjectile()
        {
            if (Object != null && Object.HasStateAuthority)
            {
               Runner.Despawn(Object);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }


        protected virtual void FixedUpdate()
        {
            if (Object != null) return; // Don't run legacy fixed update if networked
            if (movementUpdateMode == MovementUpdateMode.FixedUpdate) MovementUpdate();

        }


        protected virtual void Update()
        {
             if (Object != null) return; // Don't run legacy update if networked
            if (movementUpdateMode == MovementUpdateMode.Update) MovementUpdate();

            distanceCovered += (transform.position - lastPosition).magnitude;

            if (disableAfterLifetime)
            {
                if (Time.time - lifeStartTime > lifetime)
                {
                    DisableProjectile();
                }
            }

            if (disableAfterDistanceCovered)
            {
                if (distanceCovered >= maxDistance)
                {
                    DisableProjectile();
                }
            }

            lastPosition = transform.position;
        }


        protected virtual void OnDrawGizmosSelected()
        {
            if (areaEffect)
            {
                Color c = Gizmos.color;

                Gizmos.color = new Color(1, 0.5f, 0);
                Gizmos.DrawWireSphere(transform.position, areaEffectRadius);

                Gizmos.color = c;
            }
        }
    }
}