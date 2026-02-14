using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CardLv : MonoBehaviour
{
    [Header("勾玉预制体（外部暗图，内部亮图）")]
    public GameObject CardLvPrefab;

    [Header("等级（暗星数量）")]
    [Range(1, 5)]
    public int level = 5;

    [Header("半径")]
    public float radius = 55f;

    [Header("旋转速度")]
    public float rotationSpeed = 30f;

    [Header("角度偏移")]
    public float rotationOffset = -50f;

    [Header("当前模板名")]
    public string currentTemplateName = "Water";

    [Header("在 Start 时是否自动生成（用于在特殊流程中禁用自动生成）")]
    public bool autoGenerateOnStart = true;

    private const int MAGATAMA_COUNT = 5;

    // 缓存生成的勾玉实例
    private List<GameObject> magatamaObjects = new List<GameObject>();
    // 缓存对应的亮图GameObject，方便快速操作
    private List<GameObject> brightObjects = new List<GameObject>();

    private float currentAngle = 0f;

    void Start()
    {
        if (!autoGenerateOnStart) return;

        // 先尝试采用已有的子对象（比如拖拽复制时副本会已经包含这些子对象）
        if (!TryAdoptExistingMagatamas())
        {
            // 如果没有合适的子对象则生成
            GenerateMagatamas();
        }

        UpdateLevelDisplay();
    }

    void Update()
    {
        // 旋转整组勾玉
        currentAngle -= rotationSpeed * Time.deltaTime;
        transform.rotation = Quaternion.Euler(0, 0, currentAngle);

        // 下面代码是测试用，按数字键调等级，运行时可以删掉
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetLevel(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetLevel(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetLevel(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetLevel(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SetLevel(5);
    }

    // 尝试采用当前 transform 下已存在的勾玉实例（不会删除它们）
    // 返回 true 表示已经成功采用（并填充 magatamaObjects/brightObjects）
    bool TryAdoptExistingMagatamas()
    {
        // 若根本没有子对象，直接返回 false
        if (transform.childCount == 0) return false;

        // 收集可能是由本脚本生成的候选子对象（带标记或名字包含预制体名）
        List<Transform> candidates = new List<Transform>();
        string prefabName = CardLvPrefab != null ? CardLvPrefab.name : null;

        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child == null) continue;

            bool hasMarker = child.GetComponent("MagatamaMarker") != null;
            bool nameMatch = !string.IsNullOrEmpty(prefabName) && child.name.Contains(prefabName);
            if (hasMarker || nameMatch)
            {
                candidates.Add(child);
            }
        }

        // 要求数量与预期一致才采用；否则返回 false 以触发重新生成
        if (candidates.Count != MAGATAMA_COUNT) return false;

        // 为保证顺序一致（和生成时的角度顺序一致），按子对象在本地坐标上的角度排序
        candidates = candidates.OrderBy(t => GetAngleDegrees(t)).ToList();

        // 清空旧缓存并填充
        magatamaObjects.Clear();
        brightObjects.Clear();
        foreach (var t in candidates)
        {
            magatamaObjects.Add(t.gameObject);
            var brightChild = t.Find("亮");
            brightObjects.Add(brightChild != null ? brightChild.gameObject : null);
        }

        return true;
    }

    // 生成勾玉（若已存在会先清理匹配的旧实例）
    void GenerateMagatamas()
    {
        // 先清理之前由本脚本生成的勾玉（如果有）
        ClearGenerated();

        // 清空缓存列表
        foreach (var obj in magatamaObjects)
        {
            if (obj != null) SafeDestroy(obj);
        }
        magatamaObjects.Clear();
        brightObjects.Clear();

        // 生成
        int count = MAGATAMA_COUNT;
        float angleStep = 360f / count;
        for (int i = 0; i < count; i++)
        {
            if (CardLvPrefab == null)
            {
                Debug.LogWarning("CardLvPrefab 未设置，无法生成勾玉。");
                break;
            }

            GameObject cardObj = Instantiate(CardLvPrefab, transform, false);
            // 为便于识别，保持实例名包含预制体名（Instantiate 会自动加 (Clone)）
            // cardObj.name = CardLvPrefab.name; // 不强制改名字，保持默认即可

            magatamaObjects.Add(cardObj);

            RectTransform rt = cardObj.GetComponent<RectTransform>();
            float angle = i * angleStep;
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad)
                ) * radius;
                rt.rotation = Quaternion.Euler(0, 0, angle + rotationOffset);
            }
            else
            {
                // 非 UI 的情况，使用 localPosition
                cardObj.transform.localPosition = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad),
                    0
                ) * radius;
                cardObj.transform.localRotation = Quaternion.Euler(0, 0, angle + rotationOffset);
            }

            Transform brightChild = cardObj.transform.Find("亮");
            if (brightChild != null)
            {
                brightObjects.Add(brightChild.gameObject);
            }
            else
            {
                brightObjects.Add(null);
            }
        }
    }

    // 清理 transform 下被识别为本脚本生成的勾玉（不会删除非匹配对象）
    public void ClearGenerated()
    {
        string prefabName = CardLvPrefab != null ? CardLvPrefab.name : null;

        // 收集需要删除的 GameObject（避免在遍历时直接删除）
        List<GameObject> toDelete = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child == null) continue;

            bool hasMarker = child.GetComponent("MagatamaMarker") != null;
            bool nameMatch = !string.IsNullOrEmpty(prefabName) && child.name.Contains(prefabName);

            if (hasMarker || nameMatch)
            {
                toDelete.Add(child.gameObject);
            }
        }

        foreach (var go in toDelete)
        {
            SafeDestroy(go);
        }

        // 清空缓存引用
        magatamaObjects.Clear();
        brightObjects.Clear();
    }

    // 根据等级更新亮图显示，亮的数量 = 5 - level
    void UpdateLevelDisplay()
    {
        int brightCount = Mathf.Clamp(MAGATAMA_COUNT - level, 0, MAGATAMA_COUNT);

        // 若 brightObjects 长度不足，尝试从 magatamaObjects 找到亮子对象补充
        if (brightObjects.Count < magatamaObjects.Count)
        {
            brightObjects.Clear();
            foreach (var mag in magatamaObjects)
            {
                if (mag == null) { brightObjects.Add(null); continue; }
                var bright = mag.transform.Find("亮");
                brightObjects.Add(bright != null ? bright.gameObject : null);
            }
        }

        for (int i = 0; i < brightObjects.Count; i++)
        {
            if (brightObjects[i] != null)
                brightObjects[i].SetActive(i < brightCount);
        }

        Debug.Log($"等级:{level}，亮勾玉数量:{brightCount}");
    }

    public void SetLevel(int newLevel)
    {
        level = Mathf.Clamp(newLevel, 1, 5);
        UpdateLevelDisplay();
    }

    // Helper: 计算 transform 在本地坐标系下的角度（度）
    float GetAngleDegrees(Transform t)
    {
        RectTransform rt = t.GetComponent<RectTransform>();
        Vector2 pos;
        if (rt != null)
        {
            pos = rt.anchoredPosition;
        }
        else
        {
            Vector3 lp = t.localPosition;
            pos = new Vector2(lp.x, lp.y);
        }

        float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
        // 将角度规范到 [0,360) 以便排序
        if (angle < 0) angle += 360f;
        return angle;
    }

    // 在编辑器和运行时都安全销毁对象
    void SafeDestroy(Object obj)
    {
        if (obj == null) return;
#if UNITY_EDITOR
        // 在编辑器模式下（未播放）使用 DestroyImmediate 更合适
        if (!Application.isPlaying)
            DestroyImmediate(obj);
        else
            Destroy(obj);
#else
        Destroy(obj);
#endif
    }
}