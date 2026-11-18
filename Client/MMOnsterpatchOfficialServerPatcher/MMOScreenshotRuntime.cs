using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using Goose.Monsterpatch.GTSAllInOnePatcher;
using SocialRuntimeHost = Goose.Monsterpatch.SocialPatcher.SocialNativePatcher.SocialRuntimeHost;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Goose.Monsterpatch.Screenshots
{
    // Capture/preview and explicit authenticated community posts. The multiplayer clock is never paused.
    [DefaultExecutionOrder(-20000)]
    public sealed class MMOScreenshotRuntime : MonoBehaviour
    {
        private const int PreviewWindowId = 6152931;
        private static MMOScreenshotRuntime instance;
        private ConfigFile config;
        private ConfigEntry<float> buttonX, buttonY, buttonW, buttonH;
        private ConfigEntry<float> previewX, previewY, previewW, previewH;
        private ConfigEntry<KeyCode> captureKey, resetKey;
        private Rect buttonRect, previewRect;
        private Texture2D screenshot;
        private bool capturing, previewOpen, buttonHeld, resizingButton, moved, resizingPreview;
        private Vector2 pressPoint;
        private Rect pressRect;
        private int activeControl;
        private string status = "";
        private string savedPath = "";
        private Task<string> saveTask;
        private GameScript game;
        private GUIStyle titleStyle, hintStyle, buttonStyle, windowStyle, inputStyle, cardStyle;
        private float nextThemeRetry;
        private string caption = "";
        private bool friendsOnly = true;
        private byte[] encodedPng;
        private ScreenshotPostDraft draft = new ScreenshotPostDraft();
        private Task<string> postTask;
        private UnityScreenshotHttpHandler httpTransport;
        private string postStage = "preparing the screenshot";
        private CancellationTokenSource postCancellation;
        private readonly ConcurrentQueue<string> postProgress = new ConcurrentQueue<string>();
        private bool IsBusy { get { return saveTask != null || postTask != null; } }

        public static void EnsureHost()
        {
            if (instance != null) return;
            try
            {
                var host = new GameObject("MMOnsterpatch_Screenshot_Runtime");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<MMOScreenshotRuntime>();
            }
            catch (Exception ex) { Debug.LogWarning("[MMOnsterpatch Screenshot] Startup failed: " + ex.Message); }
        }

        public static bool CapturesInput
        {
            get
            {
                if (instance == null || !instance.isActiveAndEnabled || !instance.HasGame()) return false;
                Vector3 pointer = Input.mousePosition;
                bool pressingCamera = Input.GetMouseButton(0) && instance.buttonRect.Contains(new Vector2(pointer.x, Screen.height - pointer.y));
                return instance.previewOpen || instance.buttonHeld || instance.resizingPreview || instance.capturing || pressingCamera;
            }
        }

        private void Awake()
        {
            instance = this;
            config = new ConfigFile(Path.Combine(Paths.ConfigPath, "goose.monsterpatch.screenshots.cfg"), true);
            config.SaveOnConfigSet = false;
            buttonX = config.Bind("Camera Button", "X", 20f, "Camera button left position in screen pixels.");
            buttonY = config.Bind("Camera Button", "Y", 140f, "Camera button top position in screen pixels.");
            buttonW = config.Bind("Camera Button", "Width", 104f, "Drag the bottom-right corner to resize.");
            buttonH = config.Bind("Camera Button", "Height", 48f, "Camera button height.");
            previewX = config.Bind("Preview", "X", 160f, "Preview left position.");
            previewY = config.Bind("Preview", "Y", 80f, "Preview top position.");
            previewW = config.Bind("Preview", "Width", 760f, "Preview width.");
            previewH = config.Bind("Preview", "Height", 540f, "Preview height.");
            captureKey = config.Bind("Controls", "CaptureKey", KeyCode.F8, "Take a screenshot while playing. None disables the shortcut.");
            resetKey = config.Bind("Controls", "ResetLayoutKey", KeyCode.F9, "Reset camera button and preview position/size.");
            buttonRect = new Rect(Finite(buttonX.Value, 20), Finite(buttonY.Value, 140), Finite(buttonW.Value, 104), Finite(buttonH.Value, 48));
            previewRect = new Rect(Finite(previewX.Value, 160), Finite(previewY.Value, 80), Finite(previewW.Value, 760), Finite(previewH.Value, 540));
            ClampLayout();
            config.Save();
            Debug.Log("[MMOnsterpatch Screenshot] Ready. Click Photo or press F8; drag to move, corner to resize; F9 resets layout.");
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private bool HasGame()
        {
            if (!Goose.Monsterpatch.OfficialServer.OfficialServerSaveSelectNativeRuntime.IsOfficialOnlineModeActive()) return false;
            if (game == null) game = UnityEngine.Object.FindObjectOfType<GameScript>();
            return game != null && game.playerObj != null && game.playerObj.activeInHierarchy;
        }

        private void Update()
        {
            if (httpTransport != null) httpTransport.Pump();
            PollSave();
            PollPost();
            if (!HasGame())
            {
                if (postCancellation != null) postCancellation.Cancel();
                if (previewOpen || screenshot != null) ClosePreview();
                ReleasePointer();
                return;
            }
            if (resetKey.Value != KeyCode.None && Input.GetKeyDown(resetKey.Value)) ResetLayout();
            if (captureKey.Value != KeyCode.None && Input.GetKeyDown(captureKey.Value) && !capturing && !IsBusy)
                BeginCapture();
            if (CapturesInput) Input.ResetInputAxes();
        }

        private void LateUpdate()
        {
            // Match the existing chat window's input guard without pausing the live world.
            if (CapturesInput) Input.ResetInputAxes();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleasePointer();
        }

        private void OnGUI()
        {
            if (!HasGame() || capturing) return;
            ClampLayout();
            // Retry if the native game font was not loaded at the first draw.
            if (windowStyle == null || (titleStyle.font == null && Time.unscaledTime >= nextThemeRetry))
            {
                nextThemeRetry = Time.unscaledTime + 2f;
                GUIStyle[] theme = SocialRuntimeHost.CreateScreenshotStyles();
                if (theme == null) return;
                windowStyle = theme[0]; titleStyle = theme[1]; hintStyle = theme[2];
                buttonStyle = theme[3]; inputStyle = theme[4]; cardStyle = theme[5];
                hintStyle.richText = false;
                hintStyle.fontSize = 14;
            }
            Color previousColor = GUI.color, previousBackground = GUI.backgroundColor, previousContent = GUI.contentColor;
            bool previousEnabled = GUI.enabled;
            GUI.color = Color.white; GUI.backgroundColor = Color.white; GUI.contentColor = Color.white;
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            try
            {
                DrawCameraButton();
                if (previewOpen)
                {
                    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && !IsBusy)
                    {
                        ClosePreview();
                        Event.current.Use();
                    }
                    if (previewOpen)
                    {
                        Rect before = previewRect;
                        Rect drawn = GUI.Window(PreviewWindowId, previewRect, DrawPreview, "", windowStyle);
                        if (resizingPreview)
                        {
                            drawn.width = previewRect.width;
                            drawn.height = previewRect.height;
                        }
                        previewRect = drawn;
                        if (Event.current.rawType == EventType.MouseUp && !SameRect(before, previewRect)) SaveLayout();
                    }
                }
            }
            finally
            {
                GUI.depth = previousDepth; GUI.color = previousColor;
                GUI.backgroundColor = previousBackground; GUI.contentColor = previousContent;
                GUI.enabled = previousEnabled;
            }
        }

        private void DrawCameraButton()
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID("MMOnsterpatchPhotoButton".GetHashCode(), FocusType.Passive);
            Rect grip = new Rect(buttonRect.xMax - 20, buttonRect.yMax - 20, 20, 20);
            GUI.Box(buttonRect, new GUIContent("PHOTO", "Click: capture | Drag: move | Corner: resize | F8: capture | F9: reset"), buttonStyle);
            GUI.Label(grip, "//", hintStyle);
            bool coveredByPreview = previewOpen && previewRect.Contains(e.mousePosition);
            if (e.type == EventType.MouseDown && e.button == 0 && !coveredByPreview && buttonRect.Contains(e.mousePosition))
            {
                buttonHeld = true;
                resizingButton = grip.Contains(e.mousePosition);
                moved = false;
                pressPoint = e.mousePosition;
                pressRect = buttonRect;
                activeControl = id;
                GUIUtility.hotControl = id;
                e.Use();
            }
            else if (buttonHeld && GUIUtility.hotControl == activeControl && e.type == EventType.MouseDrag)
            {
                Vector2 delta = e.mousePosition - pressPoint;
                if (delta.sqrMagnitude > 25f) moved = true;
                if (moved || resizingButton)
                {
                    buttonRect = resizingButton
                        ? new Rect(pressRect.x, pressRect.y, pressRect.width + delta.x, pressRect.height + delta.y)
                        : new Rect(pressRect.x + delta.x, pressRect.y + delta.y, pressRect.width, pressRect.height);
                    ClampLayout();
                }
                e.Use();
            }
            else if (buttonHeld && e.type == EventType.MouseUp && e.button == 0)
            {
                bool clicked = !moved && !resizingButton && buttonRect.Contains(e.mousePosition);
                ReleasePointer();
                SaveLayout();
                e.Use();
                if (clicked && !IsBusy) BeginCapture();
            }
        }

        private void DrawPreview(int id)
        {
            float w = previewRect.width, h = previewRect.height;
            GUI.Label(new Rect(14, 8, w - 64, 28), "MMOnsterpatch Photo", titleStyle);
            GUI.enabled = !IsBusy;
            if (GUI.Button(new Rect(w - 42, 8, 28, 25), "X", buttonStyle)) ClosePreview();
            GUI.enabled = true;
            Rect imageArea = new Rect(14, 46, w - 28, Mathf.Max(60, h - 330));
            GUI.Box(imageArea, "", cardStyle);
            if (screenshot != null)
            {
                var picture = new Rect(imageArea.x + 3, imageArea.y + 3, imageArea.width - 6, imageArea.height - 6);
                GUI.DrawTexture(picture, screenshot, ScaleMode.ScaleToFit, false);
            }
            string accountName = string.IsNullOrWhiteSpace(GTSRuntimeHost.LauncherDisplayName) ? "your launcher account" : GTSRuntimeHost.LauncherDisplayName;
            GUI.Label(new Rect(14, h - 280, w - 28, 23), "Posting as: " + accountName, hintStyle);
            GUI.Label(new Rect(14, h - 256, w - 28, 22), "Caption (optional)", hintStyle);
            GUI.enabled = !IsBusy && string.IsNullOrEmpty(draft.PostId) && !draft.OutcomeUncertain;
            GUI.SetNextControlName("MMOnsterpatchScreenshotCaption");
            caption = GUI.TextArea(new Rect(14, h - 234, w - 28, 62), caption, 5000, inputStyle);
            float audienceWidth = (w - 140) / 2f;
            GUI.Label(new Rect(14, h - 166, 100, 28), "Audience:", hintStyle);
            if (GUI.Button(new Rect(112, h - 166, audienceWidth, 28), friendsOnly ? "Friends [x]" : "Friends", buttonStyle)) friendsOnly = true;
            if (GUI.Button(new Rect(120 + audienceWidth, h - 166, audienceWidth, 28), !friendsOnly ? "Everyone [x]" : "Everyone", buttonStyle)) friendsOnly = false;
            float bw = (w - 52) / 4f;
            GUI.enabled = !IsBusy;
            if (GUI.Button(new Rect(14, h - 124, bw, 32), "Retake", buttonStyle)) BeginCapture();
            GUI.enabled = !IsBusy && screenshot != null;
            if (GUI.Button(new Rect(22 + bw, h - 124, bw, 32), "Save locally", buttonStyle)) BeginSave();
            GUI.enabled = !IsBusy && screenshot != null && string.IsNullOrEmpty(draft.PostId) && !draft.OutcomeUncertain;
            string postLabel = postTask != null ? "Posting..." : !string.IsNullOrEmpty(draft.PostId) ? "Posted" : "Post to website";
            if (GUI.Button(new Rect(30 + bw * 2, h - 124, bw, 32), postLabel, buttonStyle)) BeginPost();
            GUI.enabled = saveTask == null;
            if (GUI.Button(new Rect(38 + bw * 3, h - 124, bw, 32), postTask == null ? "Close" : "Cancel", buttonStyle))
            {
                if (postTask != null) { postCancellation.Cancel(); status = "Cancelling website request..."; }
                else ClosePreview();
            }
            GUI.enabled = true;
            GUI.Label(new Rect(14, h - 86, w - 28, 48), string.IsNullOrEmpty(status) ? "Drag the title/corner to move/resize. F9 resets layout. Nothing uploads until you choose Post to website." : status, hintStyle);
            if (!string.IsNullOrEmpty(savedPath))
                GUI.TextField(new Rect(14, h - 34, w - 48, 24), savedPath, inputStyle);

            var e = Event.current;
            Rect grip = new Rect(w - 23, h - 23, 23, 23);
            GUI.Label(grip, "//", hintStyle);
            int control = GUIUtility.GetControlID("MMOnsterpatchPhotoPreviewResize".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                resizingPreview = true;
                pressPoint = GUIUtility.GUIToScreenPoint(e.mousePosition);
                pressRect = previewRect;
                activeControl = control;
                GUIUtility.hotControl = control;
                e.Use();
            }
            if (resizingPreview && e.type == EventType.MouseDrag)
            {
                Vector2 delta = GUIUtility.GUIToScreenPoint(e.mousePosition) - pressPoint;
                previewRect.width = Mathf.Clamp(pressRect.width + delta.x, Mathf.Min(560, Screen.width), Screen.width);
                previewRect.height = Mathf.Clamp(pressRect.height + delta.y, Mathf.Min(540, Screen.height), Screen.height);
                e.Use();
            }
            if (e.rawType == EventType.MouseUp)
            {
                if (resizingPreview) { ReleasePointer(); e.Use(); }
                // GUI.Window applies the final title-drag position after this callback;
                // save it on the next Update rather than persisting the previous position.
                layoutSavePending = true;
            }
            if (!resizingPreview) GUI.DragWindow(new Rect(0, 0, w - 50, 38));
        }

        private bool layoutSavePending;

        private void BeginCapture()
        {
            if (capturing || IsBusy) return;
            bool keepCaption = previewOpen && screenshot != null;
            ReleasePointer();
            SocialRuntimeHost.ReleaseInputForScreenshot();
            capturing = true;
            previewOpen = false;
            status = "";
            savedPath = "";
            if (!keepCaption) caption = "";
            encodedPng = null;
            draft = new ScreenshotPostDraft();
            StartCoroutine(CaptureFrame());
        }

        private IEnumerator CaptureFrame()
        {
            // StartCoroutine can run inside OnGUI. Wait for a complete clean frame
            // so no previous IMGUI draw of our camera/preview remains in the shot.
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D next = null;
            try
            {
                if (!HasGame()) yield break;
                Type capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule", false);
                MethodInfo method = capture == null ? null : capture.GetMethod("CaptureScreenshotAsTexture", new[] { typeof(int) });
                if (method != null) next = method.Invoke(null, new object[] { 1 }) as Texture2D;
                else
                {
                    next = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    next.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                    next.Apply();
                }
                if (next == null) throw new InvalidOperationException("The game did not return an image.");
                if (screenshot != null) Destroy(screenshot);
                screenshot = next;
                next = null;
                status = "Captured " + screenshot.width + " x " + screenshot.height + ". Choose Save locally to keep this image.";
            }
            catch (Exception ex)
            {
                status = "Capture failed: " + ErrorMessage(ex);
                Debug.LogWarning("[MMOnsterpatch Screenshot] " + status);
            }
            finally
            {
                if (next != null) Destroy(next);
                capturing = false;
                previewOpen = HasGame();
            }
        }

        private void BeginSave()
        {
            if (screenshot == null || IsBusy) return;
            try
            {
                byte[] bytes = EncodeScreenshot();
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrWhiteSpace(pictures)) pictures = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string directory = Path.Combine(pictures, "MMOnsterpatch Screenshots");
                string path = Path.Combine(directory, "MMOnsterpatch_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png");
                status = "Saving screenshot...";
                saveTask = Task.Run(() => WriteImage(directory, path, bytes));
            }
            catch (Exception ex) { status = "Save failed: " + ErrorMessage(ex); }
        }

        private byte[] EncodeScreenshot()
        {
            if (encodedPng != null) return encodedPng;
            if (screenshot == null) throw new InvalidOperationException("Take a screenshot first.");
            Type conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
            MethodInfo encode = conversion.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
            if (encode == null) throw new NotSupportedException("PNG encoding is unavailable in this game build.");
            encodedPng = encode.Invoke(null, new object[] { screenshot }) as byte[];
            if (encodedPng == null || encodedPng.Length == 0) throw new IOException("PNG encoding returned no data.");
            return encodedPng;
        }

        private void BeginPost()
        {
            if (IsBusy || screenshot == null || !string.IsNullOrEmpty(draft.PostId) || draft.OutcomeUncertain) return;
            try
            {
                byte[] png = EncodeScreenshot();
                if (png.Length > ScreenshotCommunityClient.MaximumImageBytes)
                    throw new InvalidOperationException("This image is over 8 MB. Save locally or capture at a lower resolution.");
                string accountUrl = GTSRuntimeHost.LauncherAccountBaseUrl;
                string token = !string.IsNullOrWhiteSpace(GTSRuntimeHost.LauncherSessionTokenRaw)
                    ? GTSRuntimeHost.LauncherSessionTokenRaw : GTSRuntimeHost.LauncherAioSessionTokenRaw;
                string accountId = GTSRuntimeHost.LauncherAccountId;
                string body = caption;
                bool audience = friendsOnly;
                var pendingDraft = draft;
                postCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                CancellationToken cancellation = postCancellation.Token;
                status = "Connecting to your community account...";
                postStage = "starting the website connection";
                // Construct the Unity-facing transport on the main thread. Only its
                // message queue is accessed by the background community protocol.
                var transport = new UnityScreenshotHttpHandler(this);
                httpTransport = transport;
                // Only plain image bytes, strings and draft state cross to the worker.
                // Do not use the social helper: it can return an account ID, not a token.
                postTask = Task.Run(async () =>
                {
                    using (var client = new ScreenshotCommunityClient(accountUrl, token, accountId, transport))
                        return await client.PublishAsync(png, body, audience, pendingDraft, message => postProgress.Enqueue(message), cancellation).ConfigureAwait(false);
                });
            }
            catch (Exception ex)
            {
                LogPostFailure(ex, "preparing the screenshot");
                status = "Could not post: " + ErrorMessage(ex);
            }
        }

        private void PollPost()
        {
            string message;
            while (postProgress.TryDequeue(out message)) { status = message; postStage = message; }
            if (postTask == null || !postTask.IsCompleted) return;
            if (postTask.IsFaulted || postTask.IsCanceled)
            {
                Exception error = postTask.Exception == null ? null : postTask.Exception.GetBaseException();
                LogPostFailure(error, postStage);
                if (draft.OutcomeUncertain)
                    status = "The post may have succeeded. Check your website feed before posting again.";
                else if (error is System.Net.Http.HttpRequestException || error is OperationCanceledException || postTask.IsCanceled)
                    status = "The website connection timed out or failed. Your preview is kept; you can try again.";
                else if (error is NullReferenceException)
                    status = "Posting failed during " + postStage + " Details are in the game log.";
                else status = "Could not post: " + (error == null ? "Request cancelled." : ErrorMessage(error));
            }
            else status = "Posted to the MMOnsterpatch website as your account (" + (friendsOnly ? "friends only" : "everyone") + ").";
            postTask = null;
            if (postCancellation != null) postCancellation.Dispose();
            postCancellation = null;
        }

        private static void LogPostFailure(Exception error, string stage)
        {
            // Stack traces and exception types identify runtime failures without
            // writing tokens, account responses, request headers, or raw HTTP bodies.
            var detail = new System.Text.StringBuilder("[MMOnsterpatch Screenshot] Posting failed at ").Append(stage);
            for (Exception current = error; current != null; current = current.InnerException)
                detail.Append("\n").Append(current.GetType().FullName).Append("\n").Append(current.StackTrace);
            Debug.LogWarning(detail.ToString());
        }

        private static string WriteImage(string directory, string path, byte[] bytes)
        {
            Directory.CreateDirectory(directory);
            // CreateNew prevents accidental overwrite, even after repeated capture/save clicks.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
            return path;
        }

        private void PollSave()
        {
            if (layoutSavePending) { layoutSavePending = false; SaveLayout(); }
            if (saveTask == null || !saveTask.IsCompleted) return;
            if (saveTask.IsFaulted)
            {
                status = "Save failed: " + ErrorMessage(saveTask.Exception.GetBaseException());
                Debug.LogWarning("[MMOnsterpatch Screenshot] " + status);
            }
            else
            {
                savedPath = saveTask.Result;
                status = "Screenshot saved. You can copy the path below.";
                Debug.Log("[MMOnsterpatch Screenshot] Saved: " + savedPath);
            }
            saveTask = null;
        }

        private static string ErrorMessage(Exception ex)
        {
            return ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }

        private void ClosePreview()
        {
            previewOpen = false;
            encodedPng = null;
            ReleasePointer();
            if (screenshot != null) { Destroy(screenshot); screenshot = null; }
        }

        private void ReleasePointer()
        {
            if (activeControl != 0 && GUIUtility.hotControl == activeControl) GUIUtility.hotControl = 0;
            activeControl = 0;
            buttonHeld = false;
            resizingButton = false;
            resizingPreview = false;
        }

        private void ClampLayout()
        {
            buttonRect = ClampRect(buttonRect, 60, 40, 240, 144);
            previewRect = ClampRect(previewRect, 560, 540, 1600, 1100);
        }

        private static Rect ClampRect(Rect rect, float minW, float minH, float maxW, float maxH)
        {
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            rect.width = Mathf.Clamp(rect.width, Mathf.Min(minW, sw), Mathf.Min(maxW, sw));
            rect.height = Mathf.Clamp(rect.height, Mathf.Min(minH, sh), Mathf.Min(maxH, sh));
            rect.x = Mathf.Clamp(rect.x, 0, Mathf.Max(0, sw - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0, Mathf.Max(0, sh - rect.height));
            return rect;
        }

        private void ResetLayout()
        {
            ReleasePointer();
            buttonRect = new Rect(20, 140, 104, 48);
            previewRect = new Rect((Screen.width - 760) / 2f, (Screen.height - 540) / 2f, 760, 540);
            SaveLayout();
        }

        private static bool SameRect(Rect a, Rect b)
        {
            return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
        }

        private void SaveLayout()
        {
            if (config == null) return;
            ClampLayout();
            buttonX.Value = buttonRect.x; buttonY.Value = buttonRect.y;
            buttonW.Value = buttonRect.width; buttonH.Value = buttonRect.height;
            previewX.Value = previewRect.x; previewY.Value = previewRect.y;
            previewW.Value = previewRect.width; previewH.Value = previewRect.height;
            try { config.Save(); }
            catch (Exception ex) { Debug.LogWarning("[MMOnsterpatch Screenshot] Could not save layout: " + ex.Message); }
        }

        private void OnDisable()
        {
            if (httpTransport != null) httpTransport.Dispose();
            StopAllCoroutines();
            capturing = false;
            if (postCancellation != null) postCancellation.Cancel();
            ClosePreview();
            SaveLayout();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
