// TestCommunication.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TestCommunication : MonoBehaviour
{
    [Tooltip("Reference to the MultiTeamNetworkManager in the scene.")]
    public MultiTeamNetworkManager networkManager;

    [Tooltip("Button that sends a test message.")]
    public Button sendTestMessageButton;

    [Tooltip("Text field to display received messages.")]
    public TMP_Text messageDisplay;

    void Start()
    {
        if (networkManager == null)
        {
            Debug.LogError("Network Manager is not assigned.");
            return;
        }

        // Subscribe to messages coming from the network manager.
        networkManager.OnMessageReceived += OnMessageReceived;

        // When the button is clicked, send a test message.
        if (sendTestMessageButton != null)
            sendTestMessageButton.onClick.AddListener(OnSendTestMessage);
    }

    // Called when the button is clicked.
    void OnSendTestMessage()
    {
        // Prepare a test message (you can customize this).
        string testMessage = "Test message from " + (GameSettings.Team == "" ? "Server" : GameSettings.Team);
        Debug.Log("Sending test message: " + testMessage);
        networkManager.SendActionMessage(testMessage);
    }

    // Called when a network message is received.
    void OnMessageReceived(string message)
    {
        Debug.Log("Received network message: " + message);
        if (messageDisplay != null)
        {
            // Append the received message to the display.
            messageDisplay.text += message + "\n";
        }
    }

    void OnDestroy()
    {
        // Unsubscribe to avoid potential memory leaks.
        if (networkManager != null)
            networkManager.OnMessageReceived -= OnMessageReceived;
    }
}
