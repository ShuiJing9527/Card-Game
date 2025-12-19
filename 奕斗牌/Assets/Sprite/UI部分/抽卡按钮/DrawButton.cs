using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class DrawButton : MonoBehaviour
{
    [Header("Target")]
    public Image targetImage;

    [Header("Shader property names (Reference strings)")]
    public string manualTimeProp = "_ManualTime"; // Shader Graph Reference for the manual time
    public string speedProp = "_Speed";
    public string strengthProp = "__3"; // 填入你的 Strength Reference，比如 "__3" 或 "_Strength"

    [Header("Playback")]
    public bool useManualTimeControl = false; // true => 用 manualTime 控制时间，false => 自动按 playSpeed 累加
    [Range(0f, 60f)] public float manualTime = 0f; // Inspector 滑块，实时写入材质
    public float playSpeed = 0.5f; // 自动模式下每秒推进多少“时间单位”
    public float timeScale = 1f; // 全局倍率，方便动态调整速度
    public bool paused = false;

    [Header("Strength/Play settings")]
    public float activeStrength = 1f;
    public float idleStrength = 0f;
    public bool logVerbose = false;

    private Material instanceMat;
    private Coroutine playing;

    void Awake()
    {
        if (targetImage == null)
        {
            Debug.LogError("DrawButton: targetImage 未设置");
            enabled = false;
            return;
        }

        if (targetImage.material != null)
        {
            instanceMat = new Material(targetImage.material);
            targetImage.material = instanceMat;
            if (logVerbose) Debug.Log($"DrawButton: created instance material: {instanceMat.name} shader={(instanceMat.shader ? instanceMat.shader.name : "null")}");
        }
        else
        {
            Debug.LogWarning("DrawButton: targetImage.material 为 null");
        }

        // 初始化写入当前 manualTime（避免开场跳变）
        WriteManualTimeToMaterial(manualTime);
        ResetStrengthToIdle();
    }

    void Update()
    {
        if (instanceMat == null) return;

        if (!HasPropertySafe(manualTimeProp))
            return; // 如果连 manualTimeProp 都没有，就不再继续

        if (useManualTimeControl)
        {
            // Inspector 手动滑块会直接更新 manualTime 并写入材质
            WriteManualTimeToMaterial(manualTime);
        }
        else if (!paused)
        {
            // 自动推进
            manualTime += Time.deltaTime * playSpeed * timeScale;
            WriteManualTimeToMaterial(manualTime);
        }
    }

    // 写入材质的封装（避免重复代码）
    void WriteManualTimeToMaterial(float t)
    {
        if (instanceMat == null || string.IsNullOrEmpty(manualTimeProp)) return;
        if (HasPropertySafe(manualTimeProp))
        {
            instanceMat.SetFloat(manualTimeProp, t);
        }
    }

    public void SetManualTime(float t)
    {
        manualTime = t;
        WriteManualTimeToMaterial(manualTime);
    }

    public void IncrementManualTime(float delta)
    {
        manualTime += delta;
        WriteManualTimeToMaterial(manualTime);
    }

    public void ResetTime()
    {
        manualTime = 0f;
        WriteManualTimeToMaterial(manualTime);
    }

    public void SetPlaySpeed(float sp)
    {
        playSpeed = sp;
        if (logVerbose) Debug.Log($"DrawButton: playSpeed set to {playSpeed}");
    }

    public void SetTimeScale(float scale)
    {
        timeScale = scale;
    }

    public void TogglePause()
    {
        paused = !paused;
        if (logVerbose) Debug.Log($"DrawButton: paused = {paused}");
    }

    public void SetManualMode(bool manual)
    {
        useManualTimeControl = manual;
    }

    // 简单播放一次示例（如果你还需要基于曲线播放强度，可扩展）
    public void PlayOnce()
    {
        if (playing != null) return;
        playing = StartCoroutine(PlayRoutine());
    }

    IEnumerator PlayRoutine()
    {
        float start = manualTime;
        float duration = 1.2f;
        float elapsed = 0f;

        // 触发强度（简单实现）
        if (!string.IsNullOrEmpty(strengthProp) && HasPropertySafe(strengthProp))
            instanceMat.SetFloat(strengthProp, activeStrength);

        while (elapsed < duration)
        {
            if (!paused && !useManualTimeControl)
            {
                elapsed += Time.deltaTime * playSpeed * timeScale;
                manualTime += Time.deltaTime * playSpeed * timeScale;
                WriteManualTimeToMaterial(manualTime);
            }
            yield return null;
        }

        // 恢复
        if (!string.IsNullOrEmpty(strengthProp) && HasPropertySafe(strengthProp))
            instanceMat.SetFloat(strengthProp, idleStrength);

        playing = null;
    }

    void ResetStrengthToIdle()
    {
        if (instanceMat == null) return;
        if (!string.IsNullOrEmpty(strengthProp) && HasPropertySafe(strengthProp))
            instanceMat.SetFloat(strengthProp, idleStrength);
    }

    bool HasPropertySafe(string prop)
    {
        if (instanceMat == null || string.IsNullOrEmpty(prop)) return false;
        try { return instanceMat.HasProperty(prop); }
        catch { return false; }
    }

    void OnDestroy()
    {
        if (instanceMat != null)
        {
            Destroy(instanceMat);
            instanceMat = null;
        }
    }
}
