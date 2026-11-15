using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
public class NetwrokUI : MonoBehaviour
{
    
    public Button hostButton;   
    public Button clientButton;
    void Start()
    {
        clientButton.onClick.AddListener(() => NetworkManager.Singleton.StartClient());
        hostButton.onClick.AddListener(() => NetworkManager.Singleton.StartHost());
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
