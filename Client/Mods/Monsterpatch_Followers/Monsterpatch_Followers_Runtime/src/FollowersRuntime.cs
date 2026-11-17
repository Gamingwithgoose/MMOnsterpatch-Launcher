using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Goose.Monsterpatch.FollowersRuntime
{
    [BepInPlugin("goose.monsterpatch.followersruntime", "Monsterpatch Followers Runtime", "0.4.8")]
    public sealed class FollowersRuntimePlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            FollowersRuntimeConfig.Bind(Config);
            Logger.LogInfo("Followers runtime loaded. v0.4.8 user cfg defaults + stronger movement-tier animation speed detection; preserves follower SpriteRenderer layer/order and uses vanilla z=y*0.01f sorting.");
        }
    }

    internal static class FollowersRuntimeConfig
    {
        private static ConfigEntry<float> _visualOffsetXTiles;
        private static ConfigEntry<float> _visualOffsetYTiles;
        private static ConfigEntry<float> _visualScale;
        private static ConfigEntry<float> _visualScaleX;
        private static ConfigEntry<float> _visualScaleY;
        private static ConfigEntry<float> _baseAnimationSecondsPerFrame;
        private static ConfigEntry<float> _idleAnimationSpeedMultiplier;
        private static ConfigEntry<float> _walkAnimationSpeedMultiplier;
        private static ConfigEntry<float> _runAnimationSpeedMultiplier;
        private static ConfigEntry<float> _broomAnimationSpeedMultiplier;
        private static ConfigEntry<float> _runDetectionUnitsPerSecond;
        private static ConfigEntry<float> _broomDetectionUnitsPerSecond;
        private static ConfigEntry<bool> _animationSpeedDebugLogging;
        private static ConfigEntry<int> _sortingOrderFacingLeftRightDownOffset;
        private static ConfigEntry<int> _sortingOrderFacingUpOffset;
        private static ConfigEntry<bool> _useAbsoluteTopOrderWhenFacingUp;
        private static ConfigEntry<int> _absoluteSortingOrderWhenFacingUp;
        private static bool _bound;

        public static float VisualOffsetXTiles => _visualOffsetXTiles == null ? 0f : _visualOffsetXTiles.Value;
        public static float VisualOffsetYTiles => _visualOffsetYTiles == null ? 0.6f : _visualOffsetYTiles.Value;
        public static float VisualScale => ClampPositive(_visualScale == null ? 1f : _visualScale.Value, 0.05f);
        public static float VisualScaleX => ClampPositive(_visualScaleX == null ? 1f : _visualScaleX.Value, 0.05f);
        public static float VisualScaleY => ClampPositive(_visualScaleY == null ? 1f : _visualScaleY.Value, 0.05f);
        public static float BaseAnimationSecondsPerFrame => ClampPositive(_baseAnimationSecondsPerFrame == null ? 0.28f : _baseAnimationSecondsPerFrame.Value, 0.03f);
        public static float IdleAnimationSpeedMultiplier => ClampPositive(_idleAnimationSpeedMultiplier == null ? 0.75f : _idleAnimationSpeedMultiplier.Value, 0.05f);
        public static float WalkAnimationSpeedMultiplier => ClampPositive(_walkAnimationSpeedMultiplier == null ? 1f : _walkAnimationSpeedMultiplier.Value, 0.05f);
        public static float RunAnimationSpeedMultiplier => ClampPositive(_runAnimationSpeedMultiplier == null ? 1.5f : _runAnimationSpeedMultiplier.Value, 0.05f);
        public static float BroomAnimationSpeedMultiplier => ClampPositive(_broomAnimationSpeedMultiplier == null ? 2f : _broomAnimationSpeedMultiplier.Value, 0.05f);
        public static float RunDetectionUnitsPerSecond => ClampPositive(_runDetectionUnitsPerSecond == null ? 0.35f : _runDetectionUnitsPerSecond.Value, 0.01f);
        public static float BroomDetectionUnitsPerSecond => ClampPositive(_broomDetectionUnitsPerSecond == null ? 1.0f : _broomDetectionUnitsPerSecond.Value, 0.01f);
        public static bool AnimationSpeedDebugLogging => _animationSpeedDebugLogging != null && _animationSpeedDebugLogging.Value;
        public static int SortingOrderFacingLeftRightDownOffset => _sortingOrderFacingLeftRightDownOffset == null ? -1 : _sortingOrderFacingLeftRightDownOffset.Value;
        public static int SortingOrderFacingUpOffset => _sortingOrderFacingUpOffset == null ? 1 : _sortingOrderFacingUpOffset.Value;
        public static bool UseAbsoluteTopOrderWhenFacingUp => _useAbsoluteTopOrderWhenFacingUp == null ? true : _useAbsoluteTopOrderWhenFacingUp.Value;
        public static int AbsoluteSortingOrderWhenFacingUp => _absoluteSortingOrderWhenFacingUp == null ? 20 : _absoluteSortingOrderWhenFacingUp.Value;

        private static float ClampPositive(float value, float minimum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return minimum;
            return Mathf.Max(minimum, value);
        }

        public static void Bind(ConfigFile config)
        {
            if (config == null || _bound)
                return;

            _bound = true;

            _visualOffsetXTiles = config.Bind(
                "Visual",
                "VisualOffsetXTiles",
                0f,
                "Local sprite-only X offset in Monsterpatch tile units. This moves only the rendered follower sprite, not the logical follower position.");

            _visualOffsetYTiles = config.Bind(
                "Visual",
                "VisualOffsetYTiles",
                0.6f,
                "Local sprite-only Y offset in Monsterpatch tile units. Default +0.6 matches the tuned follower battle-sprite placement from the provided cfg.");

            _visualScale = config.Bind(
                "Visual",
                "VisualScale",
                1f,
                "Uniform follower sprite scale multiplier. Default 1.0 keeps the current size; 0.8 shrinks; 1.2 enlarges.");

            _visualScaleX = config.Bind(
                "Visual",
                "VisualScaleX",
                1f,
                "Extra X-only follower sprite scale multiplier. Use this for width tuning after VisualScale.");

            _visualScaleY = config.Bind(
                "Visual",
                "VisualScaleY",
                1f,
                "Extra Y-only follower sprite scale multiplier. Use this for height tuning after VisualScale.");

            _baseAnimationSecondsPerFrame = config.Bind(
                "Animation",
                "BaseAnimationSecondsPerFrame",
                0.28f,
                "Base battle-sprite animation frame time at 1x speed. Lower is faster. Effective frame time is BaseAnimationSecondsPerFrame divided by the current movement speed multiplier.");

            _idleAnimationSpeedMultiplier = config.Bind(
                "Animation",
                "IdleAnimationSpeedMultiplier",
                0.75f,
                "Follower animation speed multiplier while idle/not catching up.");

            _walkAnimationSpeedMultiplier = config.Bind(
                "Animation",
                "WalkAnimationSpeedMultiplier",
                1f,
                "Follower animation speed multiplier while the player is walking.");

            _runAnimationSpeedMultiplier = config.Bind(
                "Animation",
                "RunAnimationSpeedMultiplier",
                1.5f,
                "Follower animation speed multiplier while the player is running. Default 1.5x.");

            _broomAnimationSpeedMultiplier = config.Bind(
                "Animation",
                "BroomAnimationSpeedMultiplier",
                2f,
                "Follower animation speed multiplier while the player is on broom/fast travel movement. Default 2x.");

            _runDetectionUnitsPerSecond = config.Bind(
                "Animation",
                "RunDetectionUnitsPerSecond",
                0.35f,
                "Fallback movement-speed detector. If reflected run/broom fields are unavailable, player movement at or above this world-units-per-second value uses RunAnimationSpeedMultiplier.");

            _broomDetectionUnitsPerSecond = config.Bind(
                "Animation",
                "BroomDetectionUnitsPerSecond",
                1.0f,
                "Fallback movement-speed detector. If reflected run/broom fields are unavailable, player movement at or above this world-units-per-second value uses BroomAnimationSpeedMultiplier.");

            _animationSpeedDebugLogging = config.Bind(
                "Animation",
                "AnimationSpeedDebugLogging",
                false,
                "When true, logs the detected follower animation tier/multiplier while moving. Use only for troubleshooting.");

            _sortingOrderFacingLeftRightDownOffset = config.Bind(
                "Visual",
                "SortingOrderFacingLeftRightDownOffsetFromPlayer",
                -1,
                "Follower SpriteRenderer sorting order relative to the player renderer when the follower is facing/moving left, right, or down. Default -1 puts the follower behind the player.");

            _sortingOrderFacingUpOffset = config.Bind(
                "Visual",
                "SortingOrderFacingUpOffsetFromPlayer",
                1,
                "Follower SpriteRenderer sorting order relative to the player renderer when the follower is facing/moving up. Default +1 puts the follower in front of the player.");

            _useAbsoluteTopOrderWhenFacingUp = config.Bind(
                "Visual",
                "UseAbsoluteTopOrderWhenFacingUp",
                true,
                "When true, upward follower travel does not use a player-relative sorting offset at all. It restores the old always-on-top style priority for upward movement instead of applying the behind-player -1 rule.");

            _absoluteSortingOrderWhenFacingUp = config.Bind(
                "Visual",
                "AbsoluteSortingOrderWhenFacingUp",
                20,
                "Absolute SpriteRenderer sorting order used while the follower is moving/facing up when UseAbsoluteTopOrderWhenFacingUp is true. This mirrors the pre-player-priority build where the follower rendered on top.");
        }

        public static void EnsureBound()
        {
            if (_bound)
                return;

            try
            {
                string path = Path.Combine(Paths.ConfigPath, "goose.monsterpatch.followersruntime.cfg");
                Bind(new ConfigFile(path, true));
                Debug.Log("[Monsterpatch Followers Runtime] Config bound from fallback path: " + path);
            }
            catch (Exception ex)
            {
                Debug.Log("[Monsterpatch Followers Runtime] Failed to bind fallback config: " + ex.Message);
            }
        }
    }


    /// <summary>
    /// Runtime half of the patcher. The preloader injects calls from PlayerController into these methods.
    /// </summary>
    public static class FollowersRuntime
    {
        private static FollowerHost _host;
        private static float _nextLogTime;

        public static void OnGameScriptStart(Component gameScript)
        {
            FollowersRuntimeConfig.EnsureBound();
            EnsureFromGameScript(gameScript);
        }

        public static void OnPlayerStart(Component player)
        {
            FollowersRuntimeConfig.EnsureBound();
            Ensure(player);
        }

        public static void OnPlayerUpdate(Component player)
        {
            FollowersRuntimeConfig.EnsureBound();
            Ensure(player);
            if (_host != null)
                _host.RuntimeTick(player);
        }

        public static void OnPlayerFaceDir(Component player, int dir)
        {
            Ensure(player);
            if (_host != null)
                _host.PlayerFaceDir(dir);
        }

        public static void OnPlayerStepBegin(Component player, Vector3 direction)
        {
            Ensure(player);
            if (_host != null)
                _host.PlayerStepBegin(player, direction);
        }

        public static void OnPlayerNewTile(Component player, Vector3 newPlayerPos)
        {
            Ensure(player);
            if (_host != null)
                _host.PlayerNewTile(player, newPlayerPos);
        }

        private static void EnsureFromGameScript(Component gameScript)
        {
            FollowersRuntimeConfig.EnsureBound();

            if (gameScript == null)
                return;

            if (_host != null)
            {
                _host.InitGameScript(gameScript);
                return;
            }

            try
            {
                var go = new GameObject("Monsterpatch_Followers_Patcher_Runtime");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _host = go.AddComponent<FollowerHost>();
                _host.InitGameScript(gameScript);
                Log("runtime host loaded from GameScript.Start.");
            }
            catch (Exception ex)
            {
                ThrottledLog("failed to create runtime host from GameScript: " + ex);
            }
        }

        private static void Ensure(Component player)
        {
            FollowersRuntimeConfig.EnsureBound();

            if (player == null)
                return;

            if (_host != null)
                return;

            try
            {
                var go = new GameObject("Monsterpatch_Followers_Patcher_Runtime");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _host = go.AddComponent<FollowerHost>();
                _host.Init(player);
                Log("runtime host loaded.");
            }
            catch (Exception ex)
            {
                ThrottledLog("failed to create runtime host: " + ex);
            }
        }

        internal static void Log(string msg)
        {
            Debug.Log("[Monsterpatch Followers Runtime] " + msg);
        }

        internal static void ThrottledLog(string msg)
        {
            if (Time.unscaledTime < _nextLogTime) return;
            _nextLogTime = Time.unscaledTime + 2f;
            Log(msg);
        }
    }

    public sealed class FollowerHost : MonoBehaviour
    {
        private const int LeadSlot = 0;
        private const float DefaultStepSize = 0.2f;
        private const float SmoothSpeed = 2.8f;
        private const float SnapDistance = 1.25f;
        private const float MirrorAbortDistance = 0.08f;
        private const float RefreshLeadSeconds = 2f;

        private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private Component _player;
        private object _gameScript;
        private GameObject _follower;
        private object _currentLead;
        private string _currentLeadKey;
        private Vector3 _targetPos;
        private bool _hasTarget;
        private int _lastFacingDir;
        private int _lastPlayerFacingDir;
        private float _nextRefreshTime;
        private SpriteAnimatorBridge _animator;
        private BattleSpriteVisualBridge _battleVisual;
        private bool _wasMoving;
        private bool _initializedPlayer;
        private bool _haveObservedPlayerPos;
        private Vector3 _lastObservedPlayerPos;
        private bool _havePlayerAnimationSamplePos;
        private Vector3 _lastPlayerAnimationSamplePos;
        private float _lastSampledPlayerUnitsPerSecond;
        private int _lastLoggedAnimationTier = -999;
        private float _nextAnimationTierLogTime;

        // While the player is actively stepping, we mirror the player's exact normalized
        // step progress instead of moving with an arbitrary follower speed. This prevents
        // the follower from visually "jumping" to the next tile on fast/short Monsterpatch steps.
        private bool _mirrorPlayerStep;
        private Vector3 _mirrorPlayerStart;
        private Vector3 _mirrorPlayerEnd;
        private Vector3 _mirrorFollowerStart;
        private Vector3 _mirrorFollowerEnd;

        // v0.4.3: render priority is driven by the follower/player's current upward travel hint,
        // not just the last idle-facing direction. The GameScript-only runtime does not always
        // receive the old PlayerController movement hooks, so fallback movement can otherwise keep
        // the old down/side priority while the follower is visibly moving upward.
        private bool _forceTopPriorityForUpTravel;

        // v0.4.4: remember the exact follower renderer layer/order from the old v0.3.5-style
        // always-on-top behavior. The previous attempts changed sortingOrder, but after any
        // side/down movement the renderer could still be stuck on the player's sorting layer.
        // When moving/facing up, restore this original layer + top order and do NOT use
        // player-relative sorting at all.
        private bool _haveOriginalFollowerSorting;
        private int _originalFollowerSortingLayerId;
        private string _originalFollowerSortingLayerName;
        private int _originalFollowerSortingOrder;

        public void InitGameScript(Component gameScript)
        {
            if (gameScript != null)
                _gameScript = gameScript;
        }

        private void Update()
        {
            if (_player == null || IsUnityNull(_player))
            {
                _player = FindPlayerController();
                if (_player == null)
                    return;
            }

            if (!_initializedPlayer)
                Init(_player);

            RuntimeTick(_player);
        }

        public void Init(Component player)
        {
            _player = player;
            _gameScript = GetMemberValue(player, "gameScript") ?? FindGameScript();
            _lastFacingDir = GetFacingDir(player);
            _lastPlayerFacingDir = _lastFacingDir;
            _targetPos = GetBehindPlayerPos(player);
            _hasTarget = true;
            _lastObservedPlayerPos = WithSortZ(player.transform.position);
            _haveObservedPlayerPos = true;
            _lastPlayerAnimationSamplePos = WithSortZ(player.transform.position);
            _havePlayerAnimationSamplePos = true;
            _lastSampledPlayerUnitsPerSecond = 0f;
            _initializedPlayer = true;
            RefreshFollower(force: true);
        }

        public void RuntimeTick(Component player)
        {
            if (player != null)
                _player = player;

            if (_player == null)
                return;

            if (_gameScript == null || IsUnityNull(_gameScript))
                _gameScript = GetMemberValue(_player, "gameScript") ?? FindGameScript();

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + RefreshLeadSeconds;
                RefreshFollower(force: false);
            }

            if (_follower == null)
                return;

            UpdatePlayerAnimationSpeedSample(_player);
            TryStartMirrorStepFromPlayerFields();

            _battleVisual?.SetAnimationSpeedMultiplier(GetBattleSpriteAnimationSpeedMultiplier(_player));
            _battleVisual?.TickIdle();

            if (!_hasTarget)
            {
                _targetPos = GetBehindPlayerPos(_player);
                _hasTarget = true;
            }

            if (_mirrorPlayerStep)
            {
                if (MirrorPlayerMovement())
                    return;
            }

            Vector3 current = _follower.transform.position;
            Vector3 target = WithSortZ(_targetPos);
            float dist = Vector2.Distance(new Vector2(current.x, current.y), new Vector2(target.x, target.y));

            if (dist > SnapDistance)
            {
                _mirrorPlayerStep = false;
                _forceTopPriorityForUpTravel = false;
                _follower.transform.position = target;
                _animator?.PlayIdle();
                ApplyFollowerRenderPriority();
                
                _wasMoving = false;
                return;
            }

            // Fallback movement path: when not using the old PlayerController hooks, the runtime
            // may move via MoveTowards. Update travel-facing/priority from the actual vector here
            // so upward travel can escape the player-relative -1 behind rule.
            if (dist > 0.005f)
                FaceFollowerTravelDirection(target - current);

            Vector3 next = Vector3.MoveTowards(current, target, SmoothSpeed * Time.deltaTime);
            next = WithSortZ(next);
            _follower.transform.position = next;
            ApplyFollowerRenderPriority();

            bool moving = Vector2.Distance(new Vector2(next.x, next.y), new Vector2(target.x, target.y)) > 0.005f;
            if (moving)
            {
                
                _animator?.PlayMoving();
                float progress = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(next.x, next.y), new Vector2(target.x, target.y)) / Mathf.Max(0.001f, GetStepSize(_player)));
                _animator?.ShowMoveProgress(progress);
            }
            else
            {
                if (_wasMoving)
                {
                    _animator?.ShowMoveProgress(1f);
                    _animator?.PlayIdle();
                }
                
            }
            _wasMoving = moving;
        }

        public void PlayerFaceDir(int dir)
        {
            // PlayerController.FaceDir fires even when the player only turns in place.
            // Do not update the follower's visual direction here; otherwise the follower
            // mimics the player's facing instead of facing only the direction it actually travels.
            _lastPlayerFacingDir = dir;
        }

        public void FaceDir(int dir)
        {
            _lastFacingDir = dir;

            // Battle sprites are side-view. We flip the renderer instead of using SpriteAnimator,
            // because the overworld SpriteAnimator expects directional sheets and can index out
            // on mon battle sprite arrays.
            _battleVisual?.FaceDir(MapFacingForSprite(dir));
            ApplyFollowerRenderPriority();
        }

        private void TryStartMirrorStepFromPlayerFields()
        {
            if (_player == null || _follower == null)
                return;

            Vector3 playerNow = WithSortZ(_player.transform.position);
            if (!_haveObservedPlayerPos)
            {
                _lastObservedPlayerPos = playerNow;
                _haveObservedPlayerPos = true;
            }

            bool playerMoving = IsPlayerMovingReflect(_player);
            if (!playerMoving)
            {
                _lastObservedPlayerPos = playerNow;
                return;
            }

            if (_mirrorPlayerStep)
                return;

            Vector3 playerStart = GetVector3Member(_player, "lastPlayerPos") ?? _lastObservedPlayerPos;
            Vector3 playerEnd = GetVector3Member(_player, "targetPos") ?? playerNow;
            playerStart = WithSortZ(playerStart);
            playerEnd = WithSortZ(playerEnd);

            float stepLen = Vector2.Distance(new Vector2(playerStart.x, playerStart.y), new Vector2(playerEnd.x, playerEnd.y));
            if (stepLen <= 0.001f)
                return;

            _mirrorPlayerStart = playerStart;
            _mirrorPlayerEnd = playerEnd;
            _mirrorFollowerStart = WithSortZ(_follower.transform.position);
            _mirrorFollowerEnd = playerStart;
            _mirrorPlayerStep = true;

            _targetPos = playerStart;
            _hasTarget = true;
            _wasMoving = true;

            // Use both the follower's travel vector and the player's step vector as upward-priority
            // hints. Some GameScript-only builds expose stale follower-facing during the first frame,
            // but playerStart -> playerEnd still tells us the step is upward.
            if (IsUpwardVector(_mirrorPlayerEnd - _mirrorPlayerStart))
                _forceTopPriorityForUpTravel = true;

            FaceFollowerTravelDirection(_mirrorFollowerEnd - _mirrorFollowerStart);
            _animator?.PlayMoving();
            _animator?.ShowMoveProgress(0f);
        }

        public void PlayerStepBegin(Component player, Vector3 direction)
        {
            if (player != null)
                _player = player;
            if (_player == null)
                return;

            // If this was only a blocked step attempt, do not let the follower walk into the player.
            if (!CanMoveReflect(_player, direction))
                return;

            RefreshFollower(force: false);
            if (_follower == null)
                return;

            // Start moving to the player's current tile as the player begins leaving it.
            // The actual visual movement is mirrored from the player's normalized step progress
            // in RuntimeTick, which keeps follower speed locked to walk/run/broom timings.
            Vector3 playerStart = WithSortZ(_player.transform.position);
            Vector3 playerEnd = WithSortZ(_player.transform.position + direction * GetStepSize(_player));
            _mirrorPlayerStart = playerStart;
            _mirrorPlayerEnd = playerEnd;
            _mirrorFollowerStart = WithSortZ(_follower.transform.position);
            _mirrorFollowerEnd = playerStart;
            _mirrorPlayerStep = true;

            _targetPos = playerStart;
            _hasTarget = true;
            _wasMoving = true;

            if (IsUpwardVector(_mirrorPlayerEnd - _mirrorPlayerStart))
                _forceTopPriorityForUpTravel = true;

            // Face the direction the follower is ACTUALLY travelling, not the player's new
            // facing/input direction. When the player turns a corner, the follower first walks
            // from its old behind-tile into the player's old tile. If we face the player input
            // direction immediately, the follower appears to moonwalk/backpedal.
            FaceFollowerTravelDirection(_mirrorFollowerEnd - _mirrorFollowerStart);
            
            _animator?.PlayMoving();
            _animator?.ShowMoveProgress(0f);
        }

        public void PlayerNewTile(Component player, Vector3 newPlayerPos)
        {
            if (player != null)
                _player = player;
            if (_player == null)
                return;

            RefreshFollower(force: false);
            if (_follower == null)
                return;

            object lastPlayerPos = GetMemberValue(_player, "lastPlayerPos");
            if (lastPlayerPos is Vector3 oldPos)
            {
                _targetPos = WithSortZ(oldPos);
            }
            else
            {
                _targetPos = WithSortZ(EstimatePreviousPlayerPos(_player, newPlayerPos));
            }

            // NewTileCheck is the end-of-step callback. Force the mirrored movement to its
            // final exact tile so tiny floating point differences do not leave the follower drifting.
            if (_mirrorPlayerStep)
            {
                _follower.transform.position = WithSortZ(_targetPos);
                _animator?.ShowMoveProgress(1f);
                _animator?.PlayIdle();
                
                _wasMoving = false;
            }
            _mirrorPlayerStep = false;
            _hasTarget = true;

            // Once the follower has landed on the player's previous tile, it is now actually
            // behind the player. Only now do we allow its idle orientation to rotate toward the
            // player's current position. This creates the corner-turn behavior the player wants:
            // travel-facing while moving, then idle-facing after landing, before the next step.
            FaceTowardPlayerFromFollowerPosition();
        }

        private bool MirrorPlayerMovement()
        {
            if (_player == null || _follower == null)
            {
                _mirrorPlayerStep = false;
                return false;
            }

            Vector3 playerNow = WithSortZ(_player.transform.position);
            Vector3 step = _mirrorPlayerEnd - _mirrorPlayerStart;
            float stepLenSq = step.x * step.x + step.y * step.y;
            if (stepLenSq <= 0.000001f)
            {
                _mirrorPlayerStep = false;
                return false;
            }

            Vector3 playerDelta = playerNow - _mirrorPlayerStart;
            float progress = Mathf.Clamp01(((playerDelta.x * step.x) + (playerDelta.y * step.y)) / stepLenSq);

            // If something else moved the player wildly, abort mirror mode and snap back to the
            // normal target-follow logic. This handles warps, cutscenes, and forced repositioning.
            Vector3 expectedPlayer = Vector3.Lerp(_mirrorPlayerStart, _mirrorPlayerEnd, progress);
            float drift = Vector2.Distance(new Vector2(playerNow.x, playerNow.y), new Vector2(expectedPlayer.x, expectedPlayer.y));
            if (drift > MirrorAbortDistance)
            {
                _mirrorPlayerStep = false;
                return false;
            }

            Vector3 followerNow = Vector3.Lerp(_mirrorFollowerStart, _mirrorFollowerEnd, progress);
            Vector3 travelNow = _mirrorFollowerEnd - _mirrorFollowerStart;
            if (IsUpwardVector(travelNow) || IsUpwardVector(_mirrorPlayerEnd - _mirrorPlayerStart))
                _forceTopPriorityForUpTravel = true;
            _follower.transform.position = WithSortZ(followerNow);
            ApplyFollowerRenderPriority();

            bool stillMoving = progress < 0.995f && IsPlayerMovingReflect(_player);
            if (stillMoving)
            {
                
                _animator?.PlayMoving();
                _animator?.ShowMoveProgress(progress);
                _wasMoving = true;
                return true;
            }

            _follower.transform.position = WithSortZ(_mirrorFollowerEnd);
            _targetPos = WithSortZ(_mirrorFollowerEnd);
            _mirrorPlayerStep = false;
            _animator?.ShowMoveProgress(1f);
            _animator?.PlayIdle();
            FaceTowardPlayerFromFollowerPosition();
            ApplyFollowerRenderPriority();
            
            _wasMoving = false;
            return false;
        }

        private void FaceFollowerTravelDirection(Vector3 travel)
        {
            if (Mathf.Abs(travel.x) <= 0.001f && Mathf.Abs(travel.y) <= 0.001f)
                return;

            _forceTopPriorityForUpTravel = IsUpwardVector(travel);
            _lastFacingDir = DirectionToFacing(travel);
            FaceDir(_lastFacingDir);
        }

        private void FaceTowardPlayerFromFollowerPosition()
        {
            if (_player == null || _follower == null)
                return;

            Vector3 toPlayer = _player.transform.position - _follower.transform.position;
            if (Mathf.Abs(toPlayer.x) <= 0.001f && Mathf.Abs(toPlayer.y) <= 0.001f)
                return;

            _forceTopPriorityForUpTravel = IsUpwardVector(toPlayer);
            _lastFacingDir = DirectionToFacing(toPlayer);
            FaceDir(_lastFacingDir);
        }

        private void RefreshFollower(bool force)
        {
            if (_player == null)
                return;

            object lead = GetLeadMon();
            if (lead == null)
            {
                if (_follower != null)
                    _follower.SetActive(false);
                return;
            }

            string key = BuildMonKey(lead);
            if (!force && _follower != null && ReferenceEquals(lead, _currentLead) && key == _currentLeadKey)
            {
                if (!_follower.activeSelf) _follower.SetActive(true);
                return;
            }

            DestroyFollower();
            _currentLead = lead;
            _currentLeadKey = key;

            Vector3 start = _hasTarget ? _targetPos : GetBehindPlayerPos(_player);
            _follower = CreateSimpleBattleSpriteFollower(lead, start);
            if (_follower == null)
                _follower = CreateFallbackPlaceholder(start);

            _follower.name = "Monsterpatch_Follower_Slot0_Patcher";
            _follower.transform.position = WithSortZ(start);
            _targetPos = WithSortZ(start);
            _hasTarget = true;
            _animator = null; // battle-sprite follower does not use the overworld SpriteAnimator
            _battleVisual = new BattleSpriteVisualBridge(_follower);
            RememberOriginalFollowerSorting();
            ApplyFollowerRenderPriority();
            FaceDir(_lastFacingDir);
            DisableColliders(_follower);
            FollowersRuntime.Log("created follower for lead slot 0. key=" + key + ", battleSprite=" + (_battleVisual.IsValid ? "yes" : "no") + ", frames=" + _battleVisual.FrameCount);
        }

        private object GetLeadMon()
        {
            if (_gameScript == null || IsUnityNull(_gameScript))
                _gameScript = GetMemberValue(_player, "gameScript") ?? FindGameScript();
            if (_gameScript == null)
                return null;

            object team = GetMemberValue(_gameScript, "teamMon")
                       ?? GetMemberValue(_gameScript, "teamMons")
                       ?? GetMemberValue(_gameScript, "playerTeam")
                       ?? GetMemberValue(_gameScript, "mons");
            if (team == null)
                return null;

            try
            {
                if (team is Array arr)
                    return arr.Length > LeadSlot ? arr.GetValue(LeadSlot) : null;

                if (team is IList list)
                    return list.Count > LeadSlot ? list[LeadSlot] : null;

                var indexer = team.GetType().GetDefaultMembers().OfType<PropertyInfo>().FirstOrDefault(p => p.GetIndexParameters().Length == 1);
                return indexer != null ? indexer.GetValue(team, new object[] { LeadSlot }) : null;
            }
            catch (Exception ex)
            {
                FollowersRuntime.ThrottledLog("GetLeadMon failed: " + ex.Message);
                return null;
            }
        }

        private GameObject CreateFollowerFromMonPrefab(object lead, Vector3 start)
        {
            try
            {
                if (_gameScript == null || lead == null)
                    return null;

                GameObject prefab = GetMemberValue(_gameScript, "monPrefab") as GameObject;
                if (prefab == null)
                {
                    FollowersRuntime.ThrottledLog("GameScript.monPrefab not found; creating simple battle-sprite visual.");
                    return CreateSimpleBattleSpriteFollower(lead, start);
                }

                GameObject go = UnityEngine.Object.Instantiate(prefab, WithSortZ(start), Quaternion.identity);
                go.name = "Monsterpatch_Follower_BattleSpriteVisual";

                Component monObject = GetComponentByName(go, "MonObject");
                if (monObject != null)
                {
                    MethodInfo init = monObject.GetType().GetMethod("InitMon", Flags);
                    if (init != null)
                        init.Invoke(monObject, new object[] { lead });
                }

                DisableGameplayBehaviours(go);
                DisableColliders(go);
                ApplyBattleSpritesToFollower(go, lead);
                return go;
            }
            catch (Exception ex)
            {
                FollowersRuntime.ThrottledLog("CreateFollowerFromMonPrefab failed: " + ex.Message);
                return CreateSimpleBattleSpriteFollower(lead, start);
            }
        }

        private GameObject CreateSimpleBattleSpriteFollower(object lead, Vector3 start)
        {
            // Keep the root transform on the logical tile position, then offset only the visual
            // child. The MMO-safe GameScript-hosted build reads player fields without touching
            // PlayerController; battle sprites render one tile south unless their visual child is
            // raised by one Monsterpatch tile. Keeping this as child-local offset preserves all
            // follower movement math while fixing the on-screen placement.
            GameObject go = new GameObject("Monsterpatch_Follower_BattleSpriteRoot");
            go.transform.position = WithSortZ(start);

            GameObject visual = new GameObject("BattleSpriteVisual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = GetVisualLocalOffset();

            SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
            // Preserve the default SpriteRenderer layer/order. World layering is handled
            // by the follower root transform's vanilla z = y * 0.01f value.
            ApplyBattleSpritesToFollower(go, lead);
            DisableColliders(go);
            return go;
        }

        private void ApplyBattleSpritesToFollower(GameObject root, object lead)
        {
            if (root == null || lead == null)
                return;

            SpriteRenderer sr = null;
            Component monObject = GetComponentByName(root, "MonObject");
            if (monObject != null)
                sr = GetMemberValue(monObject, "sr") as SpriteRenderer;
            if (sr == null)
                sr = root.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r != null);
            if (sr == null)
                return;

            object mso = GetMemberValue(lead, "monScriptableObject") ?? GetMemberValue(lead, "species") ?? GetMemberValue(lead, "speciesData");
            bool shiny = false;
            object shinyObj = GetMemberValue(lead, "isShiny") ?? GetMemberValue(lead, "shiny");
            if (shinyObj != null)
            {
                try { shiny = Convert.ToBoolean(shinyObj); } catch { shiny = false; }
            }

            Sprite[] frames = null;
            if (shiny)
                frames = GetSpriteArray(mso, "spritesShiny");
            if (frames == null || frames.Length == 0)
                frames = GetSpriteArray(mso, "sprites");
            if ((frames == null || frames.Length == 0) && shiny)
                frames = SingleSpriteArray(GetMemberValue(mso, "spriteShiny") as Sprite);
            if (frames == null || frames.Length == 0)
                frames = SingleSpriteArray(GetMemberValue(mso, "sprite") as Sprite);

            frames = CleanFrames(frames);
            frames = SelectIdleFrames(frames);
            if (frames == null || frames.Length == 0)
                return;

            ApplyVisualScale(sr);
            sr.sprite = frames[0];

            BattleSpriteIdleComponent idle = root.GetComponent<BattleSpriteIdleComponent>();
            if (idle == null)
                idle = root.AddComponent<BattleSpriteIdleComponent>();
            idle.Init(sr, frames, FollowersRuntimeConfig.BaseAnimationSecondsPerFrame);
        }

        private static Sprite[] GetSpriteArray(object obj, string fieldOrProperty)
        {
            object value = GetMemberValue(obj, fieldOrProperty);
            return value as Sprite[];
        }

        private static Sprite[] SingleSpriteArray(Sprite sprite)
        {
            return sprite == null ? null : new[] { sprite };
        }

        private static Sprite[] CleanFrames(Sprite[] frames)
        {
            if (frames == null)
                return null;
            return frames.Where(f => f != null).ToArray();
        }

        private static Sprite[] SelectIdleFrames(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0)
                return frames;

            // MonScriptableObject.sprites appears to include the full battle sequence.
            // Cycling the whole array plays attack frames, so the follower idle only uses
            // the first two battle frames. If a mon only has one valid frame, use that.
            int count = Mathf.Min(2, frames.Length);
            Sprite[] idle = new Sprite[count];
            for (int i = 0; i < count; i++)
                idle[i] = frames[i];
            return idle;
        }

        private GameObject CreateFallbackPlaceholder(Vector3 start)
        {
            GameObject go = new GameObject("Monsterpatch_Follower_PlaceholderRoot");
            go.transform.position = WithSortZ(start);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "PlaceholderVisual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = GetVisualLocalOffset();
            Vector3 placeholderScale = GetVisualLocalScale();
            visual.transform.localScale = new Vector3(0.12f * placeholderScale.x, 0.12f * placeholderScale.y, 1f);

            DisableColliders(go);
            return go;
        }

        private void DisableGameplayBehaviours(GameObject root)
        {
            foreach (Behaviour b in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null)
                    continue;

                string n = b.GetType().Name;
                if (n == "Animator" || n == "Animation" || n == "BattleSpriteIdleComponent")
                    continue;

                // Turn off MonObject/NPC/proximity/gameplay brains. The follower uses its own
                // battle-sprite idle component, not the overworld SpriteAnimator.
                try { b.enabled = false; } catch { }
            }
        }

        private void ApplyFollowerRenderPriority()
        {
            // RemoteLayerFix v0.1:
            // The vanilla player/NPC layering rule is the transform Z value:
            //     z = y * 0.01f
            // Earlier follower builds tried to force SpriteRenderer.sortingOrder relative
            // to the local player or to an absolute top order. That caused the same
            // foreground/building edge clipping problem the remote MMO players had.
            //
            // Match the successful remote-player fix: do NOT flatten or override child
            // SpriteRenderer sorting orders every frame. Let the follower keep the
            // renderer layer/order it was created with, and let WithSortZ(...) control
            // world layering.
            return;
        }

        private bool ShouldUseUpAbsoluteTopPriority()
        {
            // Direct current-travel check. This is the main fix: if the follower root is
            // physically moving upward toward its target, do not apply player-relative -1.
            if (_follower != null && _hasTarget)
            {
                Vector3 currentToTarget = WithSortZ(_targetPos) - WithSortZ(_follower.transform.position);
                if (IsUpwardVector(currentToTarget))
                    return true;
            }

            // Mirrored movement path check.
            if (_mirrorPlayerStep)
            {
                if (IsUpwardVector(_mirrorFollowerEnd - _mirrorFollowerStart))
                    return true;
                if (IsUpwardVector(_mirrorPlayerEnd - _mirrorPlayerStart))
                    return true;
            }

            if (_forceTopPriorityForUpTravel)
                return true;

            if (_lastFacingDir == 2)
                return true;

            // GameScript-only runtime fallback: if the player is stepping upward, force top.
            try
            {
                if (_player != null && !IsUnityNull(_player))
                {
                    Vector3? last = GetVector3Member(_player, "lastPlayerPos");
                    Vector3? target = GetVector3Member(_player, "targetPos");
                    if (last.HasValue && target.HasValue && IsUpwardVector(target.Value - last.Value))
                        return true;

                    if (GetFacingDir(_player) == 2)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private void RememberOriginalFollowerSorting()
        {
            _haveOriginalFollowerSorting = false;
            _originalFollowerSortingLayerId = 0;
            _originalFollowerSortingLayerName = "Default";
            _originalFollowerSortingOrder = FollowersRuntimeConfig.AbsoluteSortingOrderWhenFacingUp;

            if (_follower == null)
                return;

            try
            {
                SpriteRenderer r = _follower.GetComponentsInChildren<SpriteRenderer>(true)
                    .FirstOrDefault(x => x != null);
                if (r == null)
                    return;

                _originalFollowerSortingLayerId = r.sortingLayerID;
                _originalFollowerSortingLayerName = r.sortingLayerName;
                _originalFollowerSortingOrder = r.sortingOrder;
                _haveOriginalFollowerSorting = true;
            }
            catch { }
        }

        private int GetFacingBasedSortingOffset()
        {
            // Monsterpatch PlayerController.facingDir: 0=down, 1=left, 2=up, 3=right.
            // The follower's _lastFacingDir is updated from its own actual travel direction
            // while moving, then toward the player after it lands. This keeps sorting tied to
            // what the follower is visually doing instead of raw/player position anchors.
            if (_lastFacingDir == 2)
                return FollowersRuntimeConfig.SortingOrderFacingUpOffset;

            return FollowersRuntimeConfig.SortingOrderFacingLeftRightDownOffset;
        }

        private void DisableColliders(GameObject root)
        {
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
            foreach (Collider2D c in root.GetComponentsInChildren<Collider2D>(true))
                c.enabled = false;
        }

        private void DestroyFollower()
        {
            _animator = null;
            _battleVisual = null;
            if (_follower != null)
            {
                Destroy(_follower);
                _follower = null;
            }
        }

        private Vector3 GetVisualLocalOffset()
        {
            // This offset is intentionally based on the base Monsterpatch tile size, not
            // run/broom step length, because it is fixing sprite anchoring rather than movement.
            return new Vector3(
                FollowersRuntimeConfig.VisualOffsetXTiles * DefaultStepSize,
                FollowersRuntimeConfig.VisualOffsetYTiles * DefaultStepSize,
                0f);
        }

        private Vector3 GetVisualLocalScale()
        {
            float uniform = FollowersRuntimeConfig.VisualScale;
            return new Vector3(
                uniform * FollowersRuntimeConfig.VisualScaleX,
                uniform * FollowersRuntimeConfig.VisualScaleY,
                1f);
        }

        private void ApplyVisualScale(SpriteRenderer sr)
        {
            if (sr == null)
                return;

            Vector3 configured = GetVisualLocalScale();
            Vector3 current = sr.transform.localScale;
            float signX = current.x < 0f ? -1f : 1f;
            float signY = current.y < 0f ? -1f : 1f;
            sr.transform.localScale = new Vector3(
                signX * Mathf.Abs(current.x) * configured.x,
                signY * Mathf.Abs(current.y) * configured.y,
                current.z);
        }

        private void UpdatePlayerAnimationSpeedSample(Component player)
        {
            if (player == null)
                return;

            Vector3 now = WithSortZ(player.transform.position);
            if (!_havePlayerAnimationSamplePos)
            {
                _lastPlayerAnimationSamplePos = now;
                _havePlayerAnimationSamplePos = true;
                _lastSampledPlayerUnitsPerSecond = 0f;
                return;
            }

            float dt = Mathf.Max(0.0001f, Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime);
            float dist = Vector2.Distance(
                new Vector2(now.x, now.y),
                new Vector2(_lastPlayerAnimationSamplePos.x, _lastPlayerAnimationSamplePos.y));

            _lastSampledPlayerUnitsPerSecond = dist / dt;
            _lastPlayerAnimationSamplePos = now;
        }

        private float GetBattleSpriteAnimationSpeedMultiplier(Component player)
        {
            int tier = GetAnimationMovementTier(player);
            float multiplier;
            if (tier >= 3)
                multiplier = FollowersRuntimeConfig.BroomAnimationSpeedMultiplier;
            else if (tier == 2)
                multiplier = FollowersRuntimeConfig.RunAnimationSpeedMultiplier;
            else if (tier == 1)
                multiplier = FollowersRuntimeConfig.WalkAnimationSpeedMultiplier;
            else
                multiplier = FollowersRuntimeConfig.IdleAnimationSpeedMultiplier;

            MaybeLogAnimationTier(tier, multiplier);
            return multiplier;
        }

        private int GetAnimationMovementTier(Component player)
        {
            bool playerMoving = IsPlayerMovingForAnimation(player);
            bool sampledMoving = _lastSampledPlayerUnitsPerSecond > 0.01f;
            bool moving = _mirrorPlayerStep || _wasMoving || playerMoving || sampledMoving;
            if (!moving)
                return 0; // idle

            if (IsBroomActiveReflect(player))
                return 3;

            float moveMod = GetMoveModFloat(player);
            if (moveMod >= 1.5f)
                return 3;
            if (moveMod >= 0.5f)
                return 2;

            if (IsRunActiveReflect(player))
                return 2;

            float sampledSpeed = _lastSampledPlayerUnitsPerSecond;
            if (sampledSpeed >= FollowersRuntimeConfig.BroomDetectionUnitsPerSecond)
                return 3;
            if (sampledSpeed >= FollowersRuntimeConfig.RunDetectionUnitsPerSecond)
                return 2;

            return 1; // walking/catching up
        }

        private void MaybeLogAnimationTier(int tier, float multiplier)
        {
            if (!FollowersRuntimeConfig.AnimationSpeedDebugLogging)
                return;
            if (_lastLoggedAnimationTier == tier && Time.unscaledTime < _nextAnimationTierLogTime)
                return;

            _lastLoggedAnimationTier = tier;
            _nextAnimationTierLogTime = Time.unscaledTime + 0.75f;
            string name = tier >= 3 ? "broom" : tier == 2 ? "run" : tier == 1 ? "walk" : "idle";
            FollowersRuntime.Log("animation tier=" + name + " multiplier=" + multiplier.ToString("0.###") + " sampledSpeed=" + _lastSampledPlayerUnitsPerSecond.ToString("0.###") + " moveMod=" + GetMoveModFloat(_player).ToString("0.###"));
        }

        private int GetMoveMod(Component player)
        {
            return Mathf.RoundToInt(GetMoveModFloat(player));
        }

        private float GetMoveModFloat(Component player)
        {
            object mm = GetMemberValue(player, "moveMod")
                     ?? GetMemberValue(player, "moveModifier")
                     ?? GetMemberValue(player, "movementModifier")
                     ?? GetMemberValue(player, "speedMod")
                     ?? GetMemberValue(player, "speedModifier");
            if (mm != null)
            {
                try { return Convert.ToSingle(mm); } catch { }
            }
            return 0f;
        }

        private bool IsPlayerMovingForAnimation(Component player)
        {
            string[] names =
            {
                "isMoving", "moving", "playerMoving", "isWalking", "walking",
                "isRunning", "running", "isSprinting", "sprinting",
                "isDashing", "dashing", "isFlying", "flying", "onBroom", "usingBroom"
            };
            return AnyTruthyMember(player, names) || AnyTruthyMember(_gameScript, names);
        }

        private bool IsRunActiveReflect(Component player)
        {
            string[] names =
            {
                "isRunning", "running", "run", "isRun", "isSprinting", "sprinting",
                "isDashing", "dashing", "dash", "holdingRun", "runHeld", "fastMove"
            };
            return AnyTruthyMember(player, names) || AnyTruthyMember(_gameScript, names);
        }

        private bool IsBroomActiveReflect(Component player)
        {
            string[] names =
            {
                "onBroom", "isOnBroom", "usingBroom", "broomActive", "isBroom", "broom",
                "onBroomstick", "usingBroomstick", "broomMode", "isFlying", "flying",
                "fastTravel", "isMounted", "mounted"
            };
            return AnyTruthyMember(player, names) || AnyTruthyMember(_gameScript, names);
        }

        private bool AnyTruthyMember(object obj, string[] names)
        {
            if (obj == null || names == null)
                return false;

            for (int i = 0; i < names.Length; i++)
            {
                object v = GetMemberValue(obj, names[i]);
                if (v is bool b && b)
                    return true;
                if (v != null && !(v is bool))
                {
                    try
                    {
                        float f = Convert.ToSingle(v);
                        if (Mathf.Abs(f) > 0.001f)
                            return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private Vector3 GetBehindPlayerPos(Component player)
        {
            if (player == null)
                return Vector3.zero;

            Vector3 offset = DirectionToBehindOffset(GetFacingDir(player)) * GetStepSize(player);
            return WithSortZ(player.transform.position + offset);
        }

        private Vector3 EstimatePreviousPlayerPos(Component player, Vector3 newPlayerPos)
        {
            int dir = GetFacingDir(player);
            Vector3 behind = DirectionToBehindOffset(dir) * GetStepSize(player);
            return newPlayerPos + behind;
        }

        private static Vector3 WithSortZ(Vector3 v)
        {
            return new Vector3(v.x, v.y, v.y * 0.01f);
        }

        private float GetStepSize(Component player)
        {
            int moveMod = GetMoveMod(player);
            return DefaultStepSize * Mathf.Max(1, moveMod + 1);
        }

        private static int MapFacingForSprite(int facingDir)
        {
            // Monsterpatch PlayerController.facingDir: 0=down, 1=left, 2=up, 3=right.
            // Battle sprites are side-view. Up uses left-facing art, down uses right-facing art.
            switch (facingDir)
            {
                case 1: return 1; // left
                case 2: return 1; // up -> left
                case 3: return 3; // right
                case 0: return 3; // down -> right
                default: return 3;
            }
        }

        private static Vector3 DirectionToBehindOffset(int facingDir)
        {
            // Monsterpatch PlayerController.facingDir: 0=down, 1=left, 2=up, 3=right.
            switch (facingDir)
            {
                case 0: return Vector3.up;
                case 1: return Vector3.right;
                case 2: return Vector3.down;
                case 3: return Vector3.left;
                default: return Vector3.down;
            }
        }

        private static bool IsUpwardVector(Vector3 dir)
        {
            return dir.y > 0.001f && Mathf.Abs(dir.y) >= Mathf.Abs(dir.x);
        }

        private static int DirectionToFacing(Vector3 dir)
        {
            // Do not rely on exact Vector3 equality. Monsterpatch sometimes passes
            // normalized movement directions, but defensive axis/sign checks also handle
            // scaled directions like (0.2, 0, 0).
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
            {
                if (dir.x < -0.001f) return 1; // left
                if (dir.x >  0.001f) return 3; // right
            }
            else
            {
                if (dir.y >  0.001f) return 2; // up
                if (dir.y < -0.001f) return 0; // down
            }
            return 0;
        }

        private int GetFacingDir(Component player)
        {
            object v = GetMemberValue(player, "facingDir") ?? GetMemberValue(player?.GetType(), "facingDir");
            if (v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return _lastFacingDir;
        }

        private bool IsPlayerMovingReflect(Component player)
        {
            try
            {
                object moving = GetMemberValue(player, "isMoving");
                if (moving is bool b)
                    return b;
            }
            catch { }
            return true;
        }

        private bool CanMoveReflect(Component player, Vector3 direction)
        {
            try
            {
                MethodInfo canMove = player.GetType().GetMethod("CanMove", Flags, null, new[] { typeof(Vector3) }, null);
                if (canMove == null)
                    return true;
                object result = canMove.Invoke(player, new object[] { direction });
                return result is bool b ? b : true;
            }
            catch
            {
                return true;
            }
        }

        private Component FindPlayerController()
        {
            Type t = FindType("PlayerController");
            if (t == null)
                return null;
            return UnityEngine.Object.FindObjectOfType(t) as Component;
        }

        private Vector3? GetVector3Member(object obj, string name)
        {
            object value = GetMemberValue(obj, name);
            if (value is Vector3 v)
                return v;
            return null;
        }

        private object FindGameScript()
        {
            Type t = FindType("GameScript");
            return t == null ? null : UnityEngine.Object.FindObjectOfType(t);
        }

        private static Type FindType(string typeName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(typeName, false);
                    if (t != null) return t;
                    t = asm.GetTypes().FirstOrDefault(x => x.Name == typeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static object GetMemberValue(object objOrType, string name)
        {
            if (objOrType == null || string.IsNullOrEmpty(name))
                return null;

            try
            {
                Type type = objOrType as Type ?? objOrType.GetType();
                object instance = objOrType is Type ? null : objOrType;

                FieldInfo field = type.GetField(name, Flags);
                if (field != null)
                    return field.GetValue(field.IsStatic ? null : instance);

                PropertyInfo prop = type.GetProperty(name, Flags);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(prop.GetGetMethod(true).IsStatic ? null : instance, null);
            }
            catch { }

            return null;
        }

        private static Component GetComponentByName(GameObject root, string typeName)
        {
            if (root == null) return null;
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName) return c;
            }
            return null;
        }

        private string BuildMonKey(object mon)
        {
            if (mon == null) return "null";
            object mso = GetMemberValue(mon, "monScriptableObject") ?? GetMemberValue(mon, "species") ?? GetMemberValue(mon, "speciesData");
            object speciesName = mso == null ? null : (GetMemberValue(mso, "name") ?? GetMemberValue(mso, "monName") ?? GetMemberValue(mso, "speciesName"));
            object nickname = GetMemberValue(mon, "name") ?? GetMemberValue(mon, "nickname") ?? GetMemberValue(mon, "monName") ?? GetMemberValue(mon, "objName");
            object level = GetMemberValue(mon, "level") ?? GetMemberValue(mon, "curLevel") ?? GetMemberValue(mon, "lvl");
            return (speciesName ?? "?") + "|" + (nickname ?? "?") + "|" + (level ?? "?");
        }

        private static bool IsUnityNull(object obj)
        {
            return obj is UnityEngine.Object uo && uo == null;
        }
    }


    internal sealed class BattleSpriteVisualBridge
    {
        private readonly SpriteRenderer[] _renderers;
        private readonly BattleSpriteIdleComponent[] _idleComponents;

        public bool IsValid => _renderers != null && _renderers.Length > 0;
        public int FrameCount => _idleComponents != null && _idleComponents.Length > 0 ? _idleComponents.Max(i => i == null ? 0 : i.FrameCount) : 0;

        public BattleSpriteVisualBridge(GameObject root)
        {
            if (root == null)
            {
                _renderers = new SpriteRenderer[0];
                _idleComponents = new BattleSpriteIdleComponent[0];
                return;
            }

            _renderers = root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r != null).ToArray();
            _idleComponents = root.GetComponentsInChildren<BattleSpriteIdleComponent>(true).Where(i => i != null).ToArray();
        }

        public static bool ArtFacesLeftByDefault { get; private set; } = false;

        public static void ToggleDefaultFacing()
        {
            ArtFacesLeftByDefault = !ArtFacesLeftByDefault;
        }

        public void FaceDir(int dir)
        {
            // mapped directions: 1=left, 3=right. Up is mapped to left and down to right earlier.
            // Battle sprites appear to use a side-view base pose, and in Monsterpatch that base
            // is usually right-facing for the follower assets. If this is wrong for a specific build/asset set, press F9
            // once in-game to swap the default facing mode without rebuilding.
            bool wantLeft = dir == 1;
            bool flip = wantLeft != ArtFacesLeftByDefault;

            for (int i = 0; i < _renderers.Length; i++)
            {
                SpriteRenderer r = _renderers[i];
                if (r == null) continue;

                // SpriteRenderer.flipX handles the visual mirror without moving the follower's
                // transform, so position/movement stay stable. Also normalize localScale.x in
                // case the prefab came in with a mirrored child transform.
                Vector3 scale = r.transform.localScale;
                if (scale.x < 0f)
                    scale.x = -scale.x;
                r.transform.localScale = scale;
                r.flipX = flip;
            }
        }

        public void SetAnimationSpeedMultiplier(float multiplier)
        {
            if (_idleComponents == null)
                return;
            for (int i = 0; i < _idleComponents.Length; i++)
            {
                BattleSpriteIdleComponent idle = _idleComponents[i];
                if (idle != null)
                    idle.SetSpeedMultiplier(multiplier);
            }
        }

        public void TickIdle()
        {
            if (_idleComponents == null)
                return;
            for (int i = 0; i < _idleComponents.Length; i++)
            {
                BattleSpriteIdleComponent idle = _idleComponents[i];
                if (idle != null)
                    idle.ManualTick();
            }
        }
    }

    public sealed class BattleSpriteIdleComponent : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private float _secondsPerFrame = 0.28f;
        private float _speedMultiplier = 1f;
        private float _timer;
        private int _index;

        public int FrameCount => _frames == null ? 0 : _frames.Length;

        public void Init(SpriteRenderer renderer, Sprite[] frames, float secondsPerFrame)
        {
            _renderer = renderer;
            _frames = frames == null ? null : frames.Where(f => f != null).ToArray();
            _secondsPerFrame = Mathf.Max(0.03f, secondsPerFrame);
            _speedMultiplier = 1f;
            _timer = 0f;
            _index = 0;
            if (_renderer != null && _frames != null && _frames.Length > 0)
                _renderer.sprite = _frames[0];
        }

        public void SetSpeedMultiplier(float speedMultiplier)
        {
            if (float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier))
                speedMultiplier = 1f;
            _speedMultiplier = Mathf.Max(0.05f, speedMultiplier);
        }

        public void ManualTick()
        {
            if (_renderer == null || _frames == null || _frames.Length <= 1)
                return;

            float effectiveSecondsPerFrame = Mathf.Max(0.03f, _secondsPerFrame / Mathf.Max(0.05f, _speedMultiplier));
            _timer += Time.deltaTime;
            if (_timer < effectiveSecondsPerFrame)
                return;

            // keep remainder so the idle cycle stays smooth even if one frame is slow
            _timer %= effectiveSecondsPerFrame;
            _index++;
            if (_index >= _frames.Length)
                _index = 0;
            _renderer.sprite = _frames[_index];
        }
    }

    internal sealed class SpriteAnimatorBridge
    {
        private readonly Component _spriteAnimator;
        private readonly MethodInfo _faceDir;
        private readonly MethodInfo _playMoving;
        private readonly MethodInfo _continueMoving;
        private readonly MethodInfo _playIdle;
        private readonly MethodInfo _showMoveProgress;

        public bool IsValid => _spriteAnimator != null;

        public SpriteAnimatorBridge(GameObject root)
        {
            if (root == null) return;
            _spriteAnimator = root.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(c => c != null && c.GetType().Name == "SpriteAnimator");
            if (_spriteAnimator == null) return;

            Type t = _spriteAnimator.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _faceDir = t.GetMethod("FaceDir", flags, null, new[] { typeof(int) }, null);
            _playMoving = t.GetMethod("PlayMoving", flags, null, Type.EmptyTypes, null);
            _continueMoving = t.GetMethod("ContinueMoving", flags, null, Type.EmptyTypes, null);
            _playIdle = t.GetMethod("PlayIdle", flags, null, Type.EmptyTypes, null);
            _showMoveProgress = t.GetMethod("ShowMoveProgress", flags, null, new[] { typeof(float) }, null);
        }

        public void FaceDir(int dir)
        {
            try
            {
                SetFieldOrProperty("curDir", dir);
                _faceDir?.Invoke(_spriteAnimator, new object[] { dir });
            }
            catch { }
        }

        public void PlayMoving()
        {
            try
            {
                SetFieldOrProperty("isMoving", true);
                if (_continueMoving != null) _continueMoving.Invoke(_spriteAnimator, null);
                else _playMoving?.Invoke(_spriteAnimator, null);
            }
            catch { }
        }

        public void PlayIdle()
        {
            try
            {
                SetFieldOrProperty("isMoving", false);
                _playIdle?.Invoke(_spriteAnimator, null);
            }
            catch { }
        }

        public void ShowMoveProgress(float progress)
        {
            try { _showMoveProgress?.Invoke(_spriteAnimator, new object[] { progress }); } catch { }
        }

        private void SetFieldOrProperty(string name, object value)
        {
            if (_spriteAnimator == null) return;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = _spriteAnimator.GetType();
            FieldInfo f = t.GetField(name, flags);
            if (f != null)
            {
                f.SetValue(_spriteAnimator, value);
                return;
            }
            PropertyInfo p = t.GetProperty(name, flags);
            if (p != null && p.CanWrite)
                p.SetValue(_spriteAnimator, value, null);
        }
    }
}
