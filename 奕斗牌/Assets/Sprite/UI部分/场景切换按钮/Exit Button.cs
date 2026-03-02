using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ExitButton : MonoBehaviour
{
    public void OnLoginButtonClick()
    {
        Application.Quit();
    }
}
