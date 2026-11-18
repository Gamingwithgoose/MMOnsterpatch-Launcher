// Deterministic network/frame simulation for the transport tests. No Unity player
// assemblies, game, sockets, browser, or real website are used by this test project.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Coroutine { }
    public class MonoBehaviour
    {
        private readonly List<IEnumerator> routines = new List<IEnumerator>();
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine.MoveNext()) routines.Add(routine);
            else (routine as IDisposable)?.Dispose();
            return new Coroutine();
        }
        internal void Tick()
        {
            foreach (var routine in routines.ToArray())
                if (!routine.MoveNext()) { routines.Remove(routine); (routine as IDisposable)?.Dispose(); }
        }
    }
}
namespace UnityEngine.Networking
{
    public class DownloadHandlerBuffer { public byte[] data; }
    public class UploadHandlerRaw { public readonly byte[] data; public UploadHandlerRaw(byte[] bytes) { data = bytes; } }
    public class UnityWebRequest : IDisposable
    {
        internal static UnityWebRequest Last;
        internal static bool Hang;
        internal static long Status = 201;
        internal static byte[] Response = System.Text.Encoding.UTF8.GetBytes("{\"success\":true}");
        internal readonly string Url, Method;
        internal readonly Dictionary<string, string> RequestHeaders = new Dictionary<string, string>();
        internal bool Aborted, Disposed;
        public DownloadHandlerBuffer downloadHandler;
        public UploadHandlerRaw uploadHandler;
        public int redirectLimit, timeout;
        public bool isDone { get { return !Hang || Aborted; } }
        public long responseCode { get { return Status; } }
        public UnityWebRequest(string url, string method) { Url = url; Method = method; Last = this; }
        public void SetRequestHeader(string key, string value) { RequestHeaders[key] = value; }
        public void SendWebRequest() { downloadHandler.data = Response; }
        public void Abort() { Aborted = true; }
        public Dictionary<string, string> GetResponseHeaders() { return new Dictionary<string, string> { { "Content-Type", "application/json" } }; }
        public void Dispose() { Disposed = true; }
    }
}
