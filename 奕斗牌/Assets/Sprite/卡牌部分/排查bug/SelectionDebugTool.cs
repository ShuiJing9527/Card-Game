using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class SelectionDebugTool : MonoBehaviour
{
    [Header("Targets")]
    public RectTransform targetCard;                    // 要检查的卡牌（RectTransform）
    public MonoBehaviour selectionManagerBehaviour;     // 将 SelectionManager 的组件拖进来（类型可为 SelectionManager）
    public bool autoFindSelectionManager = true;

    [Header("Options")]
    public bool logOnStart = false;                     // 启动时打印一次
    public bool includeChildrenDetails = true;          // 是否列出子节点（Image/Canvas/Mask）详细信息
    public bool searchDragCopies = true;                // 是否在场景中搜索可能的拖拽副本（名含 "_DragCopy" 或与 targetCard 名近似）

    void Start()
    {
        if (logOnStart)
            LogDebugInfo();
    }

    [ContextMenu("Log Debug Info")]
    public void LogDebugInfo()
    {
        if (targetCard == null)
        {
            Debug.LogWarning("[SelectionDebugTool] targetCard 未设置。请在 Inspector 指定要检查的卡牌 RectTransform。");
            return;
        }

        var selMgr = GetSelectionManager();
        Debug.Log("====== SelectionDebugTool START ======");
        Debug.LogFormat("[SelectionDebugTool] Target Card: {0} (path: {1})",
            SafeName(targetCard), FullPath(targetCard));

        // Card world corners and screen rect
        PrintCardWorldAndScreenInfo(targetCard, selMgr);

        // Canvas info: parent canvas and all canvases in card children
        PrintCanvasesRelatedToRect(targetCard);

        // Images / Masks under card (optional)
        if (includeChildrenDetails) PrintImagesAndMasks(targetCard);

        // Compute suggested selection order (simulate SelectionManager logic)
        ComputeAndLogSuggestedSelectionOrder(targetCard);

        // SelectionManager / ActiveSelection info
        if (selMgr != null)
            PrintSelectionManagerInfo(selMgr);
        else
            Debug.LogWarning("[SelectionDebugTool] SelectionManager 未找到（selectionManagerBehaviour 为 null 且未自动找到）。");

        // 在场景中查找 dragCopy（可能来源于 CardDragHandler 的复制）
        if (searchDragCopies)
            FindAndPrintDragCopies(targetCard);

        Debug.Log("====== SelectionDebugTool END ======");
    }

    object GetSelectionManager()
    {
        if (selectionManagerBehaviour != null) return selectionManagerBehaviour;
        if (!autoFindSelectionManager) return null;
        // 尝试按类型名寻找（兼容你项目中可能命名的 SelectionManager 类型）
        var allBehaviours = FindObjectsOfType<MonoBehaviour>();
        foreach (var b in allBehaviours)
        {
            if (b == null) continue;
            if (b.GetType().Name == "SelectionManager")
                return b;
        }
        return null;
    }

    void PrintCardWorldAndScreenInfo(RectTransform card, object selMgr)
    {
        Camera cardCam = null;
        var parentCanvas = card.GetComponentInParent<Canvas>();
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cardCam = parentCanvas.worldCamera;

        Vector3[] corners = new Vector3[4];
        card.GetWorldCorners(corners);
        Vector3 worldCenter = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
        Vector2 blScreen = RectTransformUtility.WorldToScreenPoint(cardCam, corners[0]);
        Vector2 trScreen = RectTransformUtility.WorldToScreenPoint(cardCam, corners[2]);
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(cardCam, worldCenter);
        Vector2 screenSize = trScreen - blScreen;

        Debug.LogFormat("[Card Screen] cam:{0} screenCenter:{1} size(px):{2}",
            (cardCam != null ? cardCam.name : "null/Overlay"), screenCenter, screenSize);
    }

    void PrintCanvasesRelatedToRect(RectTransform card)
    {
        var parentCanvas = card.GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            Debug.LogFormat("[Parent Canvas] name:{0} overrideSorting:{1} sortingOrder:{2} sortingLayerID:{3} renderMode:{4} scaleFactor:{5}",
                SafeName(parentCanvas.gameObject), parentCanvas.overrideSorting, parentCanvas.sortingOrder, parentCanvas.sortingLayerID, parentCanvas.renderMode, parentCanvas.scaleFactor);
        }
        else
        {
            Debug.Log("[Parent Canvas] NONE (card not under any Canvas)");
        }

        var canvases = card.GetComponentsInChildren<Canvas>(true);
        Debug.LogFormat("[Canvases in card subtree] count:{0}", canvases.Length);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            Debug.LogFormat(" - Canvas: {0} | path:{1} | override:{2} | order:{3} | layerID:{4} | renderMode:{5}",
                SafeName(c.gameObject), FullPath(c.transform as RectTransform), c.overrideSorting, c.sortingOrder, c.sortingLayerID, c.renderMode);
        }
    }

    void PrintImagesAndMasks(RectTransform card)
    {
        var imgs = card.GetComponentsInChildren<Image>(true);
        Debug.LogFormat("[Images under card] count:{0}", imgs.Length);
        foreach (var img in imgs)
        {
            if (img == null) continue;
            var rt = img.rectTransform;
            Debug.LogFormat(" - Image: {0} | path:{1} | size:{2} | raycast:{3} | enabled:{4} | sprite:{5}",
                SafeName(img.gameObject), FullPath(rt), rt.rect.size, img.raycastTarget, img.enabled, img.sprite != null ? img.sprite.name : "null");
        }

        var masks = card.GetComponentsInChildren<Mask>(true);
        Debug.LogFormat("[Mask components under card] count:{0}", masks.Length);
        foreach (var m in masks)
        {
            if (m == null) continue;
            Debug.LogFormat(" - Mask: {0} | path:{1} | showGraphic:{2}", SafeName(m.gameObject), FullPath(m.transform as RectTransform), m.showMaskGraphic);
        }

        var rMasks = card.GetComponentsInChildren<RectMask2D>(true);
        Debug.LogFormat("[RectMask2D under card] count:{0}", rMasks.Length);
        foreach (var r in rMasks)
        {
            if (r == null) continue;
            Debug.LogFormat(" - RectMask2D: {0} | path:{1}", SafeName(r.gameObject), FullPath(r.transform as RectTransform));
        }
    }

    void ComputeAndLogSuggestedSelectionOrder(RectTransform card)
    {
        // 模拟 SelectionManager.ConfigureSelectionCanvasRelativeToCard 的逻辑，计算 cardMaxOrder 并建议 selOrder
        int cardMaxOrder = int.MinValue;
        int cardLayerID = 0;
        Canvas parentCanvas = card.GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            cardLayerID = parentCanvas.sortingLayerID;
            cardMaxOrder = Mathf.Max(cardMaxOrder, parentCanvas.sortingOrder);
        }

        var canvasesInCard = card.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvasesInCard)
        {
            if (c == null) continue;
            if (c.overrideSorting)
            {
                cardMaxOrder = Mathf.Max(cardMaxOrder, c.sortingOrder);
                cardLayerID = c.sortingLayerID;
            }
        }

        if (cardMaxOrder == int.MinValue)
        {
            cardMaxOrder = 0;
        }

        int suggestedSelOrder = Mathf.Max(0, cardMaxOrder - 1);
        Debug.LogFormat("[Suggested selection order] cardMaxOrder:{0}  suggestedSelOrder:{1}  cardLayerID:{2}",
            cardMaxOrder, suggestedSelOrder, cardLayerID);
    }

    void PrintSelectionManagerInfo(object selMgrObj)
    {
        if (selMgrObj == null) return;
        var smType = selMgrObj.GetType();
        Debug.LogFormat("[SelectionManager found] type:{0}  name:{1}", smType.Name, SafeName((selMgrObj as MonoBehaviour).gameObject));

        // 尝试获取 ActiveSelection 属性（Reflection，以兼容你现有的 SelectionManager）
        var prop = smType.GetProperty("ActiveSelection");
        RectTransform activeSel = null;
        if (prop != null)
        {
            try { activeSel = prop.GetValue(selMgrObj, null) as RectTransform; }
            catch { activeSel = null; }
        }

        if (activeSel == null)
        {
            // 尝试字段查找
            var fld = smType.GetField("activeSel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fld != null)
                activeSel = fld.GetValue(selMgrObj) as RectTransform;
        }

        if (activeSel == null)
        {
            Debug.LogWarning("[SelectionDebugTool] 未能通过反射获取 ActiveSelection（属性/字段）。请确认 SelectionManager 定义了 ActiveSelection 或 activeSel 字段。");
            return;
        }

        Debug.LogFormat("[ActiveSelection] name:{0} path:{1} active:{2}", SafeName(activeSel.gameObject), FullPath(activeSel), activeSel.gameObject.activeInHierarchy);

        var selCanvas = activeSel.GetComponent<Canvas>();
        if (selCanvas != null)
        {
            Debug.LogFormat(" - ActiveSelection Canvas: override:{0} order:{1} layerID:{2} renderMode:{3}",
                selCanvas.overrideSorting, selCanvas.sortingOrder, selCanvas.sortingLayerID, selCanvas.renderMode);
        }
        else
        {
            Debug.Log(" - ActiveSelection has NO Canvas component.");
        }

        var cg = activeSel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            Debug.LogFormat(" - CanvasGroup: blocksRaycasts:{0} interactable:{1} alpha:{2}", cg.blocksRaycasts, cg.interactable, cg.alpha);
        }

        Debug.LogFormat(" - ActiveSelection rect size:{0} anchoredPos:{1}", activeSel.rect.size, activeSel.anchoredPosition);
    }

    void FindAndPrintDragCopies(RectTransform card)
    {
        var allRoots = FindObjectsOfType<RectTransform>();
        List<GameObject> matches = new List<GameObject>();
        string cardName = SafeName(card.gameObject);

        foreach (var rt in allRoots)
        {
            if (rt == null) continue;
            var go = rt.gameObject;
            string n = go.name;
            if (n.Contains("_DragCopy") && n.Contains(cardName))
            {
                matches.Add(go);
            }
            else if (n.Contains("_DragCopy"))
            {
                // 也收集其他 dragcopy 方便检查
                matches.Add(go);
            }
            else
            {
                // 备选：名字相同但在另一个父级（注意可能误报）
                if (n == cardName && go != card.gameObject)
                {
                    matches.Add(go);
                }
            }
        }

        Debug.LogFormat("[DragCopy search] found candidates: {0}", matches.Count);
        foreach (var g in matches)
        {
            Debug.LogFormat(" - DragCandidate: {0} | path:{1}", SafeName(g), FullPath(g.transform as RectTransform));
            var canvases = g.GetComponentsInChildren<Canvas>(true);
            foreach (var c in canvases)
            {
                if (c == null) continue;
                Debug.LogFormat("    - Canvas: {0} override:{1} order:{2} layerID:{3} renderMode:{4}", SafeName(c.gameObject), c.overrideSorting, c.sortingOrder, c.sortingLayerID, c.renderMode);
            }

            var imgs = g.GetComponentsInChildren<Image>(true);
            foreach (var im in imgs)
            {
                if (im == null) continue;
                Debug.LogFormat("    - Image: {0} | path:{1} | size:{2} | raycast:{3}", SafeName(im.gameObject), FullPath(im.rectTransform), im.rectTransform.rect.size, im.raycastTarget);
            }
        }
    }

    // Utilities
    string FullPath(RectTransform rt)
    {
        if (rt == null) return "null";
        var parts = new List<string>();
        Transform cur = rt.transform;
        while (cur != null)
        {
            parts.Insert(0, cur.name);
            cur = cur.parent;
        }
        return string.Join("/", parts.ToArray());
    }

    string SafeName(Object o)
    {
        if (o == null) return "null";
        return o.name;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SelectionDebugTool))]
public class SelectionDebugToolEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SelectionDebugTool t = (SelectionDebugTool)target;
        GUILayout.Space(6);
        if (GUILayout.Button("Log Debug Info"))
        {
            t.LogDebugInfo();
        }

        if (GUILayout.Button("Find SelectionManager in Scene"))
        {
            var all = FindObjectsOfType<MonoBehaviour>();
            MonoBehaviour found = null;
            foreach (var b in all)
            {
                if (b == null) continue;
                if (b.GetType().Name == "SelectionManager")
                {
                    found = b;
                    break;
                }
            }
            if (found != null)
            {
                t.selectionManagerBehaviour = found;
                EditorUtility.SetDirty(t);
                Debug.Log("[SelectionDebugToolEditor] Found and assigned SelectionManager: " + found.name);
            }
            else
            {
                Debug.LogWarning("[SelectionDebugToolEditor] 未找到名为 SelectionManager 的组件。");
            }
        }
    }
}
#endif
