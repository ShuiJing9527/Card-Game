using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class DrawCardButton : MonoBehaviour
{
    public void OnLoginButtonClick()
    {
        SceneManager.LoadScene(1);
    }
}
