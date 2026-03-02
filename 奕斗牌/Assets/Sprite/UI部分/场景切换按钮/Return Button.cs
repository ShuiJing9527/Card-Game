using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ReturnButton : MonoBehaviour
{
    public void OnLoginButtonClick()
    {
        SceneManager.LoadScene(0);
    }
}
