using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbusPaladinRangeFix;

/// <summary>
/// Investigation build only. All operations are read-only, local and bounded.
/// Incremental scene discovery is capped per frame; never run a global
/// Resources.FindObjectsOfTypeAll sweep, which previously stalled VR.
/// </summary>
public sealed class MechanicsDiagnostics : MonoBehaviour
{
    public MechanicsDiagnostics(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    private const int F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A;
    private const int NodesPerFrame = 12;
    private const int MaxSceneNodes = 18000;
    private const int MaxDetails = 200;
    private const int MaxSnapshotNodes = 380;

    private readonly Queue<Transform> _sceneQueue = new();
    private readonly Queue<(Transform Node, int Depth)> _snapshotQueue = new();
    private readonly HashSet<int> _seenCandidates = new();
    private readonly StringBuilder _pending = new(8192);
    private readonly Dictionary<string, Vector3> _lastPositions = new();

    private string _report = "";
    private string _mode = "";
    private float _endTime;
    private float _nextSample;
    private float _nextPlayerSearch;
    private float _nextFlush;
    private GameObject? _player;
    private Camera? _camera;
    private int _sceneNodes;
    private int _detailCount;
    private int _snapshotNodes;
    private int _markers;
    private bool _oldF8, _oldF9, _oldF10, _oldF11;

    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(empty)";
        string s = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return s.Length <= 110 ? s : s.Substring(0, 110) + "...";
    }
    private static bool Interesting(string name)
    {
        return name.IndexOf("paladin", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("hammer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("teleport", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("mount", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("summon", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("riding", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void Start()
    {
        try
        {
            string folder = Path.Combine(Paths.BepInExRootPath, "MechanicsDiagnostics");
            Directory.CreateDirectory(folder);
            _report = Path.Combine(folder,
                "Mechanics_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
            Line("OrbusPaladinRangeFix v0.1.0 - investigation only");
            Line("UTC " + DateTimeOffset.UtcNow.ToString("O"));
            Line("Read-only; no injected teleports, mounts, movement or network calls.");
            Line("F8 Paladin, F9 mount/combat, F10 hierarchy, F11 manual event marker.");
            Line("This report may contain local Unity object names; review before sharing.");
            Flush();
            Plugin.Logger.LogInfo("[PaladinRangeFix] Diagnostic report: " + _report);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[PaladinRangeFix] Cannot create diagnostic file: " + ex.Message);
        }
    }

    private void Update()
    {
        if (!Plugin.Enabled.Value) return;

        bool focused = Application.isFocused;
        bool f8 = focused && Down(F8);
        bool f9 = focused && Down(F9);
        bool f10 = focused && Down(F10);
        bool f11 = focused && Down(F11);
        if (f8 && !_oldF8) Begin("PALADIN", Plugin.PaladinSeconds.Value);
        if (f9 && !_oldF9) Begin("MOUNT_COMBAT", Plugin.MountSeconds.Value);
        if (f10 && !_oldF10) Snapshot("MANUAL_F10");
        if (f11 && !_oldF11) Marker();
        _oldF8 = f8; _oldF9 = f9; _oldF10 = f10; _oldF11 = f11;

        // Snapshot work is also incremental: no long synchronous tree walks.
        PumpSnapshot();
        if (_mode.Length > 0)
        {
            PumpScene();
            if (Time.realtimeSinceStartup >= _nextSample)
            {
                _nextSample = Time.realtimeSinceStartup + Plugin.SampleInterval.Value;
                Sample();
            }
            if (Time.realtimeSinceStartup >= _endTime)
            {
                Line("=== END " + _mode + " at " + DateTimeOffset.UtcNow.ToString("O") +
                     "; scannedSceneNodes=" + _sceneNodes + " candidateDetails=" + _detailCount + " ===");
                Plugin.Logger.LogInfo("[PaladinRangeFix] Finished " + _mode + " recording. See " + _report);
                _mode = "";
                Flush();
            }
        }
        if (Time.realtimeSinceStartup >= _nextFlush || _pending.Length >= 24000)
        {
            _nextFlush = Time.realtimeSinceStartup + 1.5f;
            Flush();
        }
    }

    private void Begin(string mode, int seconds)
    {
        if (_mode.Length > 0)
            Line("Previous " + _mode + " recording interrupted by " + mode + ".");
        _mode = mode;
        _endTime = Time.realtimeSinceStartup + seconds;
        _nextSample = 0f;
        _sceneNodes = 0;
        _detailCount = 0;
        _sceneQueue.Clear();
        _seenCandidates.Clear();
        _lastPositions.Clear();

        FindPlayer(true);
        Line("=== BEGIN " + mode + " " + seconds + " seconds UTC " +
             DateTimeOffset.UtcNow.ToString("O") + " ===");
        Line("LOCAL_PLAYER=" + (_player == null ? "NOT FOUND" : Clean(_player.name)));
        Line("ACTIVE_SCENE=" + Clean(SceneManager.GetActiveScene().name));
        Snapshot("START_" + mode);

        try
        {
            var scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root != null) _sceneQueue.Enqueue(root.transform);
            Line("Queued scene roots=" + _sceneQueue.Count +
                 ". Traversal spread across frames; max " + MaxSceneNodes + " nodes.");
        }
        catch (Exception ex)
        {
            Line("Scene root enumeration unavailable: " + Clean(ex.Message));
        }
        Flush();
    }

    private void FindPlayer(bool force = false)
    {
        if (!force && _player != null) return;
        if (!force && Time.realtimeSinceStartup < _nextPlayerSearch) return;
        _nextPlayerSearch = Time.realtimeSinceStartup + 1.5f;
        try { _player = GameObject.Find("PlayerRigv4"); } catch { _player = null; }
        if (_camera == null)
        {
            try
            {
                foreach (Camera candidate in Camera.allCameras)
                {
                    if (candidate != null && candidate.isActiveAndEnabled &&
                        candidate.stereoEnabled && candidate.name == "Camera (eye)")
                    {
                        _camera = candidate;
                        break;
                    }
                }
            }
            catch { }
        }
    }

    private Vector3 Anchor()
    {
        if (_camera != null) return _camera.transform.position;
        if (_player != null) return _player.transform.position;
        return Vector3.zero;
    }

    private static Transform? Child(Transform root, string name)
    {
        try { return root.Find(name); }
        catch { return null; }
    }

    private void Sample()
    {
        try
        {
            FindPlayer();
            if (_player == null)
            {
                Line("SAMPLE player rig not currently found.");
                return;
            }
            Transform root = _player.transform;
            Watch("rig", root);
            string[] paths =
            {
                "Avatar", "Avatar/Paladin(Clone)",
                "Controller (left)", "Controller (right)",
                "Controller (left)/Hammer", "Controller (right)/Hammer",
                "Controller (left)/Weapon", "Controller (right)/Weapon"
            };
            foreach (string path in paths)
            {
                Transform? part = Child(root, path);
                if (part != null) Watch(path, part);
            }
        }
        catch (Exception ex) { Line("SAMPLE error: " + Clean(ex.Message)); }
    }

    private void Watch(string label, Transform target)
    {
        Vector3 pos = target.position;
        if (_lastPositions.TryGetValue(label, out Vector3 previous))
        {
            float delta = Vector3.Distance(previous, pos);
            if (delta > 0.7f)
                Line("MOVE " + label + " delta=" + delta.ToString("0.00") + "m" +
                     " pos=" + Position(pos));
        }
        else Line("TRACK " + label + " at " + Position(pos));
        _lastPositions[label] = pos;
    }

    private static string Position(Vector3 v) =>
        v.x.ToString("0.00") + "," + v.y.ToString("0.00") + "," + v.z.ToString("0.00");

    private void PumpScene()
    {
        int budget = NodesPerFrame;
        while (budget-- > 0 && _sceneQueue.Count > 0 && _sceneNodes < MaxSceneNodes)
        {
            Transform node = _sceneQueue.Dequeue();
            if (node == null) continue;
            _sceneNodes++;
            try
            {
                string name = node.name;
                if (Interesting(name) && _detailCount < MaxDetails &&
                    (_player == null || Vector3.Distance(node.position, Anchor()) < 38f))
                {
                    int id = node.gameObject.GetInstanceID();
                    if (_seenCandidates.Add(id))
                    {
                        _detailCount++;
                        Describe(node, "DISCOVERED");
                    }
                }
                int children = Math.Min(node.childCount, 120);
                for (int i = 0; i < children; i++)
                {
                    if (_sceneQueue.Count >= MaxSceneNodes) break;
                    Transform child = node.GetChild(i);
                    if (child != null) _sceneQueue.Enqueue(child);
                }
            }
            catch (Exception ex) { Line("SCENE node error: " + Clean(ex.Message)); }
        }
    }

    private void Snapshot(string label)
    {
        FindPlayer();
        Line("=== SNAPSHOT " + label + " UTC " + DateTimeOffset.UtcNow.ToString("O") + " ===");
        if (_player == null)
        {
            Line("No local PlayerRigv4 found.");
            return;
        }
        _snapshotQueue.Clear();
        _snapshotNodes = 0;
        Transform rig = _player.transform;
        AddSnapshot(rig, 0);
        string[] paths =
        {
            "Avatar/Paladin(Clone)", "Avatar",
            "Controller (left)", "Controller (right)",
            "Camera (eye)"
        };
        foreach (string path in paths)
        {
            Transform? node = Child(rig, path);
            if (node != null) AddSnapshot(node, 0);
        }
        Line("Snapshot queues local rig, avatar, class and controllers; incremental traversal.");
    }

    private void AddSnapshot(Transform target, int depth)
    {
        if (_snapshotQueue.Count < MaxSnapshotNodes)
            _snapshotQueue.Enqueue((target, depth));
    }

    private void PumpSnapshot()
    {
        int budget = NodesPerFrame;
        while (budget-- > 0 && _snapshotQueue.Count > 0 && _snapshotNodes < MaxSnapshotNodes)
        {
            var entry = _snapshotQueue.Dequeue();
            Transform node = entry.Node;
            if (node == null) continue;
            _snapshotNodes++;
            try
            {
                if (entry.Depth < 2 || Interesting(node.name))
                    Describe(node, "LOCAL depth=" + entry.Depth);
                if (entry.Depth >= 6) continue;
                int children = Math.Min(node.childCount, 75);
                for (int i = 0; i < children; i++)
                    AddSnapshot(node.GetChild(i), entry.Depth + 1);
            }
            catch (Exception ex) { Line("SNAPSHOT error: " + Clean(ex.Message)); }
        }
        if (_snapshotNodes >= MaxSnapshotNodes) _snapshotQueue.Clear();
    }

    private static string PathOf(Transform node)
    {
        var parts = new List<string>(10);
        int count = 0;
        while (node != null && count++ < 9)
        {
            parts.Add(Clean(node.name));
            node = node.parent;
        }
        parts.Reverse();
        return (node != null ? ".../" : "") + string.Join("/", parts);
    }

    private static string ComponentTypes(GameObject go)
    {
        var names = new List<string>(12);
        try
        {
            foreach (Component component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                if (names.Count == 12) break;
                string type = component.GetType().Name;
                try
                {
                    IntPtr klass = IL2CPP.il2cpp_object_get_class(component.Pointer);
                    if (klass != IntPtr.Zero)
                    {
                        string className = IL2CPP.il2cpp_class_get_name_(klass) ?? "";
                        string ns = IL2CPP.il2cpp_class_get_namespace_(klass) ?? "";
                        if (className.Length > 0) type = (ns.Length > 0 ? ns + "." : "") + className;
                    }
                }
                catch { }
                names.Add(Clean(type));
            }
        }
        catch (Exception ex) { names.Add("types unavailable: " + Clean(ex.Message)); }
        return string.Join(", ", names);
    }

    private void Describe(Transform node, string prefix)
    {
        try
        {
            Line(prefix + " path=" + PathOf(node) +
                 " pos=" + Position(node.position) +
                 " active=" + node.gameObject.activeInHierarchy +
                 " components=[" + ComponentTypes(node.gameObject) + "]");
        }
        catch (Exception ex) { Line(prefix + " detail failed: " + Clean(ex.Message)); }
    }

    private void Marker()
    {
        _markers++;
        FindPlayer();
        Line("=== EVENT MARKER " + _markers + " mode=" +
             (_mode.Length == 0 ? "idle" : _mode) + " UTC " +
             DateTimeOffset.UtcNow.ToString("O") +
             " headset=" + Position(Anchor()) + " ===");
        Plugin.Logger.LogInfo("[PaladinRangeFix] Event marker " + _markers);
        Flush();
    }

    private void Line(string line)
    {
        _pending.Append('[').Append(Time.realtimeSinceStartup.ToString("0.000"))
            .Append("s] ").AppendLine(line);
    }

    private void Flush()
    {
        if (_pending.Length == 0 || _report.Length == 0) return;
        try
        {
            File.AppendAllText(_report, _pending.ToString());
            _pending.Clear();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[PaladinRangeFix] Report write failed: " + ex.Message);
            _pending.Clear();
        }
    }

    private void OnDestroy()
    {
        Line("Component destroyed; mode=" + _mode);
        Flush();
    }
}
