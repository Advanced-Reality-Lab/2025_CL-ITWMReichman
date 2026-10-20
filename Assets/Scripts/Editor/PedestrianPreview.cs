using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Lets the pedestrians walk in the Scene view without entering Play mode.
// Toggle with Tools/Euro Blocks/Preview Pedestrians Walking; turning it off puts everyone back.
[InitializeOnLoad]
public static class PedestrianPreview
{
    const string MenuPath = "Tools/Euro Blocks/Preview Pedestrians Walking";

    class Saved { public CityWalker w; public float dist; public Vector3 pos; public Quaternion rot; }

    static bool _on;
    static double _last, _start;
    static readonly List<Saved> _saved = new List<Saved>();

    static PedestrianPreview()
    {
        EditorApplication.playModeStateChanged += s => Stop();
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
    }

    [MenuItem(MenuPath)]
    static void Toggle() { if (_on) Stop(); else Start(); }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, _on); return !EditorApplication.isPlaying; }

    static void Start()
    {
        _saved.Clear();
        foreach (var w in Object.FindObjectsByType<CityWalker>(FindObjectsSortMode.None))
            _saved.Add(new Saved { w = w, dist = w.distance, pos = w.transform.position, rot = w.transform.rotation });
        if (_saved.Count == 0) { Debug.Log("No pedestrians in the scene."); return; }
        _on = true;
        _start = _last = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Debug.Log("Previewing " + _saved.Count + " pedestrians. Use the same menu item to stop.");
    }

    static void Stop()
    {
        if (!_on) return;
        _on = false;
        EditorApplication.update -= Tick;
        foreach (var s in _saved)
        {
            if (s.w == null) continue;
            s.w.distance = s.dist;
            s.w.transform.SetPositionAndRotation(s.pos, s.rot);
            Pose(s.w, 0f);
        }
        _saved.Clear();
        SceneView.RepaintAll();
    }

    static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Min(0.1f, (float)(now - _last));
        _last = now;
        float t = (float)(now - _start);
        foreach (var s in _saved)
        {
            if (s.w == null) continue;
            s.w.Step(dt);
            Pose(s.w, t * s.w.speed / 1.3f);
        }
        SceneView.RepaintAll();
    }

    static void Pose(CityWalker w, float t)
    {
        foreach (var anim in w.GetComponentsInChildren<Animation>())
        {
            var clip = anim.clip;
            if (clip == null) continue;
            clip.SampleAnimation(anim.gameObject, Mathf.Repeat(t, clip.length));
        }
    }
}
