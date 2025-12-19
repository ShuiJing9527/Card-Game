using System.Collections.Generic;
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

    // 缓存生成的勾玉实例
    private List<GameObject> magatamaObjects = new List<GameObject>();
    // 缓存对应的亮图GameObject，方便快速操作
    private List<GameObject> brightObjects = new List<GameObject>();

    private float currentAngle = 0f;

    void Start()
    {
        GenerateMagatamas();
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

    // 生成5个勾玉排列
    void GenerateMagatamas()
    {
        // 清除旧的
        foreach (var obj in magatamaObjects)
            if (obj != null) Destroy(obj);
        magatamaObjects.Clear();
        brightObjects.Clear();

        int count = 5;
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            GameObject cardObj = Instantiate(CardLvPrefab, transform);
            magatamaObjects.Add(cardObj);

            // 设置位置和旋转
            RectTransform rt = cardObj.GetComponent<RectTransform>();
            float angle = i * angleStep;
            rt.anchoredPosition = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad)
            ) * radius;
            rt.rotation = Quaternion.Euler(0, 0, angle + rotationOffset);

            // 找到子物体“亮”，缓存以便控制显示
            Transform brightChild = cardObj.transform.Find("亮");
            if (brightChild != null)
            {
                brightObjects.Add(brightChild.gameObject);
            }
            else
            {
                Debug.LogWarning($"预制体第{i}个实例中没有找到名为“亮”的子物体");
                brightObjects.Add(null);
            }
        }
    }

    public void SetLevel(int newLevel)
    {
        level = Mathf.Clamp(newLevel, 1, 5);
        UpdateLevelDisplay();
    }

    // 根据等级更新亮图显示，亮的数量 = 5 - level
    void UpdateLevelDisplay()
    {
        int brightCount = 5 - level;

        for (int i = 0; i < brightObjects.Count; i++)
        {
            if (brightObjects[i] != null)
                brightObjects[i].SetActive(i < brightCount);
        }
        Debug.Log($"等级:{level}，亮勾玉数量:{brightCount}");
    }
}