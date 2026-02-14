using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;

public class EventSystemFinder : EditorWindow
{
    [MenuItem("Tools/EventSystem Finder")]
    static void Open()
    {
        GetWindow<EventSystemFinder>("EventSystem Finder");
    }

    void OnGUI()
    {
        if (GUILayout.Button("List EventSystems in Scene"))
        {
            var all = FindObjectsOfType<EventSystem>();
            Debug.LogFormat("Found {0} EventSystem(s) in scene.", all.Length);
            foreach (var e in all)
            {
                Debug.LogFormat(" - {0} (path: {1})", e.name, GetFullPath(e.transform));
            }
        }

        if (GUILayout.Button("Delete Extra EventSystems (keep 1)"))
        {
            var all = FindObjectsOfType<EventSystem>();
            if (all.Length <= 1)
            {
                Debug.Log("No extra EventSystems to delete.");
            }
            else
            {
                // Keep the first one, delete others
                for (int i = 1; i < all.Length; i++)
                {
                    Debug.Log("Deleting EventSystem: " + all[i].name + " (path: " + GetFullPath(all[i].transform) + ")");
                    DestroyImmediate(all[i].gameObject);
                }
            }
        }
    }

    string GetFullPath(Transform t)
    {
        if (t == null) return "null";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}