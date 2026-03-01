using UnityEngine;

public class OverlayWatcher : MonoBehaviour
{
    RectTransform rt;
    Vector2 lastAnchored;
    Vector3 lastWorldPos;
    Transform lastParent;
    Vector2 lastSize;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        lastAnchored = rt.anchoredPosition;
        lastWorldPos = rt.position;
        lastParent = transform.parent;
        lastSize = rt.sizeDelta;
    }

    void Update()
    {
        if (rt == null) return;
        if (rt.anchoredPosition != lastAnchored || transform.parent != lastParent || rt.sizeDelta != lastSize || rt.position != lastWorldPos)
        {
            Debug.Log($"OverlayWatcher: change detected at {Time.time:F2} | parent={transform.parent?.name} anchored={rt.anchoredPosition} size={rt.sizeDelta} worldPos={rt.position}");
            lastAnchored = rt.anchoredPosition;
            lastParent = transform.parent;
            lastSize = rt.sizeDelta;
            lastWorldPos = rt.position;
        }
    }
}