using HarmonyLib;
using BepInEx;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Goose.Monsterpatch.GTSAllInOnePatcher;

namespace Goose.Monsterpatch.HomeMailbox
{
    /// <summary>
    /// Client-only world mailbox access point for the existing MMOnsterpatch mail system.
    /// This does not add server commands or database tables. It replaces the visible My Town
    /// barrel near the player house with an embedded mailbox sprite when possible, and falls
    /// back to spawning a barrel-prefab-based mailbox if that barrel is not found.
    /// </summary>
    public sealed class MMOHomeMailboxRuntime : MonoBehaviour
    {
        private const string HostObjectName = "MMOnsterpatch_HomeMailbox_Runtime";
        private const string MailboxObjectName = "MMOnsterpatch_HomeMailbox";
        private const float ScanIntervalSeconds = 0.75f;

        // Defaults are intentionally config-driven now. The first mailbox attempt guessed from
        // scene anchors and could pick the wrong decorative barrel. This version targets a single
        // configurable My Town barrel position first, then lets Goose tune X/Y/scale without code.
        internal const float DefaultTargetX = -4.0f;
        internal const float DefaultTargetY = 24.0f;
        internal const float DefaultTargetZ = 0f;
        internal const float DefaultReplaceRadius = 1.0f;
        internal const float DefaultMailboxPixelsPerUnit = 160f;
        internal const float DefaultMailboxScale = 1.6f;
        internal const float DefaultColliderWidth = 0.18f;
        internal const float DefaultColliderHeight = 0.18f;
        internal static readonly Vector3 DefaultParentHouseMailboxOffset = new Vector3(-0.8f, 0.004f, 0f);

        private static MMOHomeMailboxRuntime _instance;
        private static Sprite _mailboxSprite;
        private static bool _loggedMissingSprite;
        private float _nextScanAt;
        private GameObject _mailboxObject;
        private GameObject _lastReplacedObject;
        private bool _loggedCandidateBarrels;

        public static void EnsureHost()
        {
            try
            {
                HomeMailboxConfig.EnsureBound();
                if (!HomeMailboxConfig.Enabled)
                    return;
                if (_instance != null)
                    return;

                GameObject go = new GameObject(HostObjectName);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<MMOHomeMailboxRuntime>();
                GTSNativePatcher.RuntimeLog("Home mailbox runtime loaded from GameScript.Start.");
            }
            catch (Exception ex)
            {
                GTSNativePatcher.RuntimeWarn("Home mailbox runtime EnsureHost failed: " + ex.Message);
            }
        }

        public static bool TryHandleInteract(GameScript gs, int dir)
        {
            try
            {
                HomeMailboxConfig.EnsureBound();
                if (!HomeMailboxConfig.Enabled)
                    return false;
                if (gs == null)
                    return false;
                if (GameScript.interacting || GameScript.inCutscene || MenuScript.gameState != MenuScript.GameState.Open)
                    return false;
                if (gs.dialogueManager != null && gs.dialogueManager.menuDialogue != null && gs.dialogueManager.menuDialogue.activeSelf)
                    return false;

                RaycastHit2D hit = RaycastFrontTile(gs, dir);
                if (!hit)
                    return false;

                GameObject obj = hit.transform != null ? hit.transform.gameObject : null;
                if (!IsMailboxObject(obj))
                    return false;

                GameScript.interacting = true;
                GTSRuntimeHost.OpenMailboxFromWorld();
                gs.StartCoroutine(DelayedInteractionOff());
                return true;
            }
            catch (Exception ex)
            {
                GTSNativePatcher.RuntimeWarn("Home mailbox interaction failed: " + ex.Message);
                return false;
            }
        }

        private static System.Collections.IEnumerator DelayedInteractionOff()
        {
            yield return new WaitForSeconds(0.15f);
            GameScript.interacting = false;
        }

        private static RaycastHit2D RaycastFrontTile(GameScript gs, int dir)
        {
            Vector3 playerPos = gs.playerObj != null ? gs.playerObj.transform.position : Vector3.zero;
            Vector3 probe;
            if (dir == 0) probe = playerPos + new Vector3(0f, -0.2f, 0f);
            else if (dir == 1) probe = playerPos + new Vector3(-0.2f, 0f, 0f);
            else if (dir == 2) probe = playerPos + new Vector3(0f, 0.2f, 0f);
            else probe = playerPos + new Vector3(0.2f, 0f, 0f);

            return Physics2D.Raycast(probe, Vector2.zero, 99f, gs.layerEverything);
        }

        private static bool IsMailboxObject(GameObject obj)
        {
            if (obj == null)
                return false;
            if (obj.GetComponent<MMOHomeMailboxMarker>() != null)
                return true;
            string name = obj.name ?? string.Empty;
            return name.IndexOf(MailboxObjectName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Awake()
        {
            _instance = this;
            HomeMailboxConfig.EnsureBound();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            try
            {
                HomeMailboxConfig.EnsureBound();
                if (!HomeMailboxConfig.Enabled)
                    return;
                if (Time.realtimeSinceStartup < _nextScanAt)
                    return;
                _nextScanAt = Time.realtimeSinceStartup + ScanIntervalSeconds;

                GameScript gs = UnityEngine.Object.FindObjectOfType<GameScript>();
                if (gs == null || gs.playerObj == null)
                    return;

                // My Town is the only intended mailbox location for this first pass.
                if (!GameScript.inPlayerTown)
                {
                    _loggedCandidateBarrels = false;
                    return;
                }

                EnsureMailboxInTown(gs);
            }
            catch (Exception ex)
            {
                GTSNativePatcher.RuntimeWarn("Home mailbox maintenance failed: " + ex.Message);
            }
        }

        private void EnsureMailboxInTown(GameScript gs)
        {
            Sprite mailboxSprite = GetMailboxSprite();
            if (mailboxSprite == null)
            {
                if (!_loggedMissingSprite)
                {
                    _loggedMissingSprite = true;
                    GTSNativePatcher.RuntimeWarn("Embedded MailboxIcon.png was not found; home mailbox visual was not applied.");
                }
                return;
            }

            if (_mailboxObject != null && _mailboxObject.activeInHierarchy)
            {
                ApplyMailboxVisual(_mailboxObject, mailboxSprite);
                return;
            }

            GameObject existingMarker = FindExistingMailboxMarker();
            if (existingMarker != null)
            {
                _mailboxObject = existingMarker;
                ApplyMailboxVisual(_mailboxObject, mailboxSprite);
                return;
            }

            Vector3 target = ResolveMailboxTargetPosition(gs);
            GameObject barrel = HomeMailboxConfig.ReplaceNearbyBarrel ? FindBestBarrelCandidate(target) : null;
            if (barrel != null)
            {
                _mailboxObject = barrel;
                _lastReplacedObject = barrel;
                ApplyMailboxVisual(barrel, mailboxSprite);
                GTSNativePatcher.RuntimeLog("Home mailbox replaced My Town barrel at " + FormatVec(barrel.transform.position) + " using target " + FormatVec(target) + ", radius=" + HomeMailboxConfig.ReplaceRadius.ToString("0.00") + ".");
                return;
            }

            if (!HomeMailboxConfig.SpawnIfMissing)
            {
                GTSNativePatcher.RuntimeWarn("Home mailbox did not find a barrel within radius " + HomeMailboxConfig.ReplaceRadius.ToString("0.00") + " of " + FormatVec(target) + "; standalone spawn is disabled.");
                return;
            }

            _mailboxObject = SpawnMailboxFromBarrelPrefab(gs, target, mailboxSprite);
            if (_mailboxObject != null)
                GTSNativePatcher.RuntimeLog("Home mailbox spawned at " + FormatVec(_mailboxObject.transform.position) + " because no nearby My Town barrel was found.");
        }

        private static GameObject FindExistingMailboxMarker()
        {
            MMOHomeMailboxMarker[] markers = UnityEngine.Object.FindObjectsOfType<MMOHomeMailboxMarker>();
            foreach (MMOHomeMailboxMarker marker in markers)
            {
                if (marker != null && marker.gameObject != null && marker.gameObject.activeInHierarchy)
                    return marker.gameObject;
            }
            return null;
        }

        private static Vector3 ResolveMailboxTargetPosition(GameScript gs)
        {
            HomeMailboxConfig.EnsureBound();
            if (HomeMailboxConfig.UseParentHouseAnchor)
            {
                Transform houseAnchor = FindParentsHouseAnchor();
                if (houseAnchor != null)
                    return houseAnchor.position + new Vector3(HomeMailboxConfig.AnchorOffsetX, HomeMailboxConfig.AnchorOffsetY, HomeMailboxConfig.TargetZ);
            }

            return new Vector3(HomeMailboxConfig.TargetX, HomeMailboxConfig.TargetY, HomeMailboxConfig.TargetZ);
        }

        private static Transform FindParentsHouseAnchor()
        {
            try
            {
                Transform[] all = UnityEngine.Object.FindObjectsOfType<Transform>();
                Transform best = null;
                float bestScore = float.MaxValue;
                foreach (Transform t in all)
                {
                    if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy)
                        continue;
                    string n = t.name ?? string.Empty;
                    if (n.IndexOf("ParentsHouse", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("PlayerTown_ParentsHouse", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    Vector3 p = t.position;
                    float score = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(MMOHomeMailboxRuntime.DefaultTargetX, MMOHomeMailboxRuntime.DefaultTargetY));
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = t;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        private GameObject FindBestBarrelCandidate(Vector3 target)
        {
            try
            {
                HomeMailboxConfig.EnsureBound();
                SpriteRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
                GameObject best = null;
                float bestDist = float.MaxValue;
                System.Text.StringBuilder debug = HomeMailboxConfig.DebugLogCandidateBarrels && !_loggedCandidateBarrels
                    ? new System.Text.StringBuilder()
                    : null;

                foreach (SpriteRenderer sr in renderers)
                {
                    if (sr == null || sr.gameObject == null || !sr.gameObject.activeInHierarchy)
                        continue;
                    GameObject obj = sr.gameObject;
                    if (IsMailboxObject(obj))
                        return obj;
                    if (!LooksLikeBarrel(obj, sr))
                        continue;
                    Vector3 p = obj.transform.position;
                    if (!LooksLikePlayerTownPosition(p))
                        continue;

                    float dist = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(target.x, target.y));
                    if (debug != null && dist <= HomeMailboxConfig.DebugLogRadius)
                        debug.AppendLine("  barrel candidate name='" + (obj.name ?? "") + "' sprite='" + (sr.sprite != null ? (sr.sprite.name ?? "") : "") + "' pos=" + FormatVec(p) + " dist=" + dist.ToString("0.00"));

                    if (dist <= HomeMailboxConfig.ReplaceRadius && dist < bestDist)
                    {
                        best = obj;
                        bestDist = dist;
                    }
                }

                if (debug != null)
                {
                    _loggedCandidateBarrels = true;
                    GTSNativePatcher.RuntimeLog("Home mailbox target=" + FormatVec(target) + ", replaceRadius=" + HomeMailboxConfig.ReplaceRadius.ToString("0.00") + ". Nearby My Town barrel candidates:\n" + debug.ToString().TrimEnd());
                }

                return best;
            }
            catch { return null; }
        }

        private static bool LooksLikePlayerTownPosition(Vector3 p)
        {
            // Matches the vanilla broad My Town check while allowing the barrel/house to sit
            // close to the boundary/collider edge.
            return p.x > -4f && p.x < 12f && p.y > 20f && p.y < 32f;
        }

        private static bool LooksLikeBarrel(GameObject obj, SpriteRenderer sr)
        {
            string objectName = obj != null ? (obj.name ?? string.Empty) : string.Empty;
            string spriteName = sr != null && sr.sprite != null ? (sr.sprite.name ?? string.Empty) : string.Empty;
            return objectName.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   spriteName.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GameObject SpawnMailboxFromBarrelPrefab(GameScript gs, Vector3 target, Sprite mailboxSprite)
        {
            try
            {
                GameObject obj = null;
                if (gs != null && gs.barrelObj != null)
                {
                    obj = UnityEngine.Object.Instantiate(gs.barrelObj, target, Quaternion.identity);
                }
                else
                {
                    obj = new GameObject(MailboxObjectName);
                    obj.transform.position = target;
                }
                ApplyMailboxVisual(obj, mailboxSprite);
                return obj;
            }
            catch (Exception ex)
            {
                GTSNativePatcher.RuntimeWarn("Home mailbox spawn failed: " + ex.Message);
                return null;
            }
        }

        private static void ApplyMailboxVisual(GameObject obj, Sprite mailboxSprite)
        {
            if (obj == null || mailboxSprite == null)
                return;

            HomeMailboxConfig.EnsureBound();
            obj.name = MailboxObjectName;
            obj.transform.localScale = new Vector3(HomeMailboxConfig.Scale, HomeMailboxConfig.Scale, obj.transform.localScale.z == 0f ? 1f : obj.transform.localScale.z);

            MMOHomeMailboxMarker marker = obj.GetComponent<MMOHomeMailboxMarker>();
            if (marker == null)
                marker = obj.AddComponent<MMOHomeMailboxMarker>();
            marker.IsHomeMailbox = true;

            SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
            SpriteRenderer[] childRenderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
            if (sr == null && childRenderers != null && childRenderers.Length > 0)
                sr = childRenderers[0];
            if (sr == null)
                sr = obj.AddComponent<SpriteRenderer>();

            if (HomeMailboxConfig.DisableExtraChildRenderers && childRenderers != null)
            {
                foreach (SpriteRenderer extra in childRenderers)
                {
                    if (extra != null && extra != sr)
                        extra.enabled = false;
                }
            }

            sr.sprite = mailboxSprite;
            sr.enabled = true;

            BoxCollider2D box = obj.GetComponent<BoxCollider2D>();
            if (box == null)
                box = obj.AddComponent<BoxCollider2D>();
            box.size = new Vector2(HomeMailboxConfig.ColliderWidth, HomeMailboxConfig.ColliderHeight);
            if (box.isTrigger)
                box.isTrigger = false;
        }

        private static Sprite GetMailboxSprite()
        {
            try
            {
                if (_mailboxSprite != null)
                    return _mailboxSprite;

                Assembly asm = typeof(MMOHomeMailboxRuntime).Assembly;
                string resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("MailboxIcon.png", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(resourceName))
                    return null;

                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;
                    byte[] data = new byte[stream.Length];
                    int read = stream.Read(data, 0, data.Length);
                    if (read <= 0)
                        return null;

                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    if (!LoadPngIntoTexture(tex, data))
                    {
                        UnityEngine.Object.Destroy(tex);
                        return null;
                    }

                    _mailboxSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), HomeMailboxConfig.PixelsPerUnit);
                    _mailboxSprite.name = "MMOnsterpatch_HomeMailbox_Sprite";
                    return _mailboxSprite;
                }
            }
            catch { return null; }
        }


        private static bool LoadPngIntoTexture(Texture2D tex, byte[] data)
        {
            try
            {
                Type imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imageConversionType != null)
                {
                    MethodInfo loadImage = imageConversionType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                    if (loadImage != null)
                    {
                        object result = loadImage.Invoke(null, new object[] { tex, data, false });
                        return result is bool b ? b : true;
                    }
                    loadImage = imageConversionType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]) });
                    if (loadImage != null)
                    {
                        object result = loadImage.Invoke(null, new object[] { tex, data });
                        return result is bool b ? b : true;
                    }
                }

                MethodInfo instanceLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]) });
                if (instanceLoadImage != null)
                {
                    object result = instanceLoadImage.Invoke(tex, new object[] { data });
                    return result is bool b ? b : true;
                }
                instanceLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]), typeof(bool) });
                if (instanceLoadImage != null)
                {
                    object result = instanceLoadImage.Invoke(tex, new object[] { data, false });
                    return result is bool b ? b : true;
                }
            }
            catch { }
            return false;
        }

        private static string FormatVec(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }
    }

    internal static class HomeMailboxConfig
    {
        private static bool _bound;
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _replaceNearbyBarrel;
        private static ConfigEntry<bool> _spawnIfMissing;
        private static ConfigEntry<float> _targetX;
        private static ConfigEntry<float> _targetY;
        private static ConfigEntry<float> _targetZ;
        private static ConfigEntry<float> _replaceRadius;
        private static ConfigEntry<float> _scale;
        private static ConfigEntry<float> _pixelsPerUnit;
        private static ConfigEntry<float> _colliderWidth;
        private static ConfigEntry<float> _colliderHeight;
        private static ConfigEntry<bool> _useParentHouseAnchor;
        private static ConfigEntry<float> _anchorOffsetX;
        private static ConfigEntry<float> _anchorOffsetY;
        private static ConfigEntry<bool> _disableExtraChildRenderers;
        private static ConfigEntry<bool> _debugLogCandidateBarrels;
        private static ConfigEntry<float> _debugLogRadius;

        public static bool Enabled => _enabled == null || _enabled.Value;
        public static bool ReplaceNearbyBarrel => _replaceNearbyBarrel == null || _replaceNearbyBarrel.Value;
        public static bool SpawnIfMissing => _spawnIfMissing == null || _spawnIfMissing.Value;
        public static float TargetX => Safe(_targetX, MMOHomeMailboxRuntime.DefaultTargetX);
        public static float TargetY => Safe(_targetY, MMOHomeMailboxRuntime.DefaultTargetY);
        public static float TargetZ => Safe(_targetZ, MMOHomeMailboxRuntime.DefaultTargetZ);
        public static float ReplaceRadius => Mathf.Max(0.05f, Safe(_replaceRadius, MMOHomeMailboxRuntime.DefaultReplaceRadius));
        public static float Scale => Mathf.Max(0.05f, Safe(_scale, MMOHomeMailboxRuntime.DefaultMailboxScale));
        public static float PixelsPerUnit => Mathf.Max(1f, Safe(_pixelsPerUnit, MMOHomeMailboxRuntime.DefaultMailboxPixelsPerUnit));
        public static float ColliderWidth => Mathf.Max(0.01f, Safe(_colliderWidth, MMOHomeMailboxRuntime.DefaultColliderWidth));
        public static float ColliderHeight => Mathf.Max(0.01f, Safe(_colliderHeight, MMOHomeMailboxRuntime.DefaultColliderHeight));
        public static bool UseParentHouseAnchor => _useParentHouseAnchor == null || _useParentHouseAnchor.Value;
        public static float AnchorOffsetX => Safe(_anchorOffsetX, MMOHomeMailboxRuntime.DefaultParentHouseMailboxOffset.x);
        public static float AnchorOffsetY => Safe(_anchorOffsetY, MMOHomeMailboxRuntime.DefaultParentHouseMailboxOffset.y);
        public static bool DisableExtraChildRenderers => _disableExtraChildRenderers == null || _disableExtraChildRenderers.Value;
        public static bool DebugLogCandidateBarrels => _debugLogCandidateBarrels != null && _debugLogCandidateBarrels.Value;
        public static float DebugLogRadius => Mathf.Max(0.1f, Safe(_debugLogRadius, 2.0f));

        public static void EnsureBound()
        {
            if (_bound)
                return;
            _bound = true;
            try
            {
                string path = Path.Combine(Paths.ConfigPath, "goose.monsterpatch.home_mailbox.cfg");
                ConfigFile cfg = new ConfigFile(path, true);
                _enabled = cfg.Bind("Home Mailbox", "Enabled", true, "Enable the physical mailbox in My Town.");
                _replaceNearbyBarrel = cfg.Bind("Home Mailbox", "ReplaceNearbyBarrel", true, "When true, replace the existing barrel nearest TargetX/TargetY with the mailbox sprite.");
                _spawnIfMissing = cfg.Bind("Home Mailbox", "SpawnIfMissing", true, "When true, spawn a standalone mailbox at TargetX/TargetY if no nearby barrel is found.");
                _targetX = cfg.Bind("Placement", "TargetX", MMOHomeMailboxRuntime.DefaultTargetX, "World X coordinate for the My Town mailbox/barrel target. Tweak this to line up with the barrel beside the blue-roof player house.");
                _targetY = cfg.Bind("Placement", "TargetY", MMOHomeMailboxRuntime.DefaultTargetY, "World Y coordinate for the My Town mailbox/barrel target. Tweak this to line up with the barrel beside the blue-roof player house.");
                _targetZ = cfg.Bind("Placement", "TargetZ", MMOHomeMailboxRuntime.DefaultTargetZ, "World Z coordinate for the mailbox target. Usually leave at 0.");
                _replaceRadius = cfg.Bind("Placement", "ReplaceRadius", MMOHomeMailboxRuntime.DefaultReplaceRadius, "How close a barrel must be to TargetX/TargetY before it can be replaced. Keep this tight so the wrong doorway barrel is not selected.");
                _scale = cfg.Bind("Visual", "Scale", MMOHomeMailboxRuntime.DefaultMailboxScale, "Mailbox object scale. Use this to make the mailbox icon bigger/smaller in-world.");
                _pixelsPerUnit = cfg.Bind("Visual", "PixelsPerUnit", MMOHomeMailboxRuntime.DefaultMailboxPixelsPerUnit, "Pixels-per-unit used when creating the embedded mailbox sprite. Higher values make the sprite smaller.");
                _disableExtraChildRenderers = cfg.Bind("Visual", "DisableExtraChildRenderers", true, "When replacing a barrel prefab with child sprites, disables extra child renderers so the original barrel art does not remain visible.");
                _colliderWidth = cfg.Bind("Interaction", "ColliderWidth", MMOHomeMailboxRuntime.DefaultColliderWidth, "Mailbox interaction collider width in world units.");
                _colliderHeight = cfg.Bind("Interaction", "ColliderHeight", MMOHomeMailboxRuntime.DefaultColliderHeight, "Mailbox interaction collider height in world units.");
                _useParentHouseAnchor = cfg.Bind("Advanced", "UseParentHouseAnchor", true, "If true, computes mailbox position from the detected ParentsHouse anchor plus AnchorOffsetX/Y. Default true for the confirmed My Town player-house mailbox placement.");
                _anchorOffsetX = cfg.Bind("Advanced", "AnchorOffsetX", MMOHomeMailboxRuntime.DefaultParentHouseMailboxOffset.x, "Only used when UseParentHouseAnchor=true.");
                _anchorOffsetY = cfg.Bind("Advanced", "AnchorOffsetY", MMOHomeMailboxRuntime.DefaultParentHouseMailboxOffset.y, "Only used when UseParentHouseAnchor=true.");
                _debugLogCandidateBarrels = cfg.Bind("Debug", "LogNearbyBarrelsOnce", true, "Logs nearby My Town barrel candidates once. Use this to tune TargetX/TargetY if the mailbox replaces the wrong barrel.");
                _debugLogRadius = cfg.Bind("Debug", "LogRadius", 2.0f, "Radius around TargetX/TargetY for one-time candidate barrel logging.");
                cfg.Save();
                GTSNativePatcher.RuntimeLog("Home mailbox config loaded: " + path);
            }
            catch (Exception ex)
            {
                GTSNativePatcher.RuntimeWarn("Home mailbox config load failed; using built-in defaults. " + ex.Message);
            }
        }

        private static float Safe(ConfigEntry<float> entry, float fallback)
        {
            if (entry == null)
                return fallback;
            float value = entry.Value;
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value;
        }
    }

    public sealed class MMOHomeMailboxMarker : MonoBehaviour
    {
        public bool IsHomeMailbox;
    }

    [HarmonyPatch(typeof(GameScript), "Interact")]
    public static class MMOHomeMailbox_GameScript_Interact_Patch
    {
        public static bool Prefix(GameScript __instance, int dir)
        {
            return !MMOHomeMailboxRuntime.TryHandleInteract(__instance, dir);
        }
    }
}
