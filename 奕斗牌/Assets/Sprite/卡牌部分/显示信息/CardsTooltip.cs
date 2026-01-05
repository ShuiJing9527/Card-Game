using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class CardsTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [Header("区域绑定（可在 Inspector 指定；若为空会尝试自动查找）")]
    public RectTransform effectZone;
    public RectTransform bondZone;
    public RectTransform targetCardRoot;

    [Header("显示文本（可由外部代码赋值）")]
    [TextArea] public string titleText;
    [TextArea] public string effectText;
    [TextArea] public string bondText;

    [Header("偏移（像素）")]
    public float verticalOffset = 8f;

    [Header("Tooltip 控制器（优先使用 Inspector 指定；否则使用 TooltipController.Instance）")]
    public TooltipController tooltipController;

    bool isOver = false;
    Canvas parentCanvas;

    void Awake()
    {
        if (targetCardRoot == null) targetCardRoot = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (tooltipController == null && TooltipController.Instance != null)
            tooltipController = TooltipController.Instance;

        // 自动尝试绑定 effectZone / bondZone：按场景中 TextMeshProUGUI 的顺序
        if (effectZone == null || bondZone == null)
        {
            var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmps != null && tmps.Length > 0)
            {
                if (effectZone == null) effectZone = tmps[0].rectTransform;
                if (bondZone == null && tmps.Length > 1) bondZone = tmps[1].rectTransform;
            }
        }
    }

    void UpdateTooltipByScreenPos(Vector2 screenPos, Camera cam)
    {
        var tc = tooltipController ?? TooltipController.Instance;
        if (tc == null)
        {
            Debug.LogWarning($"CardsTooltip ({name}): TooltipController not found.");
            return;
        }

        // 优先显示羁绊区域（bond），其次显示效果区域（effect）
        if (bondZone != null && RectTransformUtility.RectangleContainsScreenPoint(bondZone, screenPos, cam))
        {
            tc.ShowAbove(bondZone, titleText, bondText, null, verticalOffset, true);
            return;
        }

        if (effectZone != null && RectTransformUtility.RectangleContainsScreenPoint(effectZone, screenPos, cam))
        {
            tc.ShowAbove(effectZone, titleText, effectText, null, verticalOffset, true);
            return;
        }

        tc.Hide();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isOver = true;
        Camera cam = eventData.enterEventCamera ?? (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null);
        UpdateTooltipByScreenPos(eventData.position, cam);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!isOver) return;
        Camera cam = eventData.enterEventCamera ?? (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null);
        UpdateTooltipByScreenPos(eventData.position, cam);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isOver = false;
        var tc = tooltipController ?? TooltipController.Instance;
        if (tc != null) tc.Hide();
    }
}
