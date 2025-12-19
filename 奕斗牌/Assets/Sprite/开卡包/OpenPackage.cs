using UnityEngine;

public class OpenPackage : MonoBehaviour
{
    public GameObject cardPrefab;

    CardDrawStore CardDrawStore;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CardDrawStore = GetComponent<CardDrawStore>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnClickOpen()
    {
        for (int i = 0; i < 5; i++)
        {
            GameObject newCard = GameObject.Instantiate(cardPrefab);
        }
    }
}
