using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class DeckButton : MonoBehaviour
{
    public void OnLoginButtonClick()
    {
        SceneManager.LoadScene(2);
    }
}
