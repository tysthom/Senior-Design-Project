// MenuManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    // Assign these via the Inspector.
    public Button serverButton;
    public Button redClientButton;
    public Button blueClientButton;

    void Start()
    {
        if (serverButton != null)
            serverButton.onClick.AddListener(OnSelectServer);
        if (redClientButton != null)
            redClientButton.onClick.AddListener(() => OnSelectClient("Red"));
        if (blueClientButton != null)
            blueClientButton.onClick.AddListener(() => OnSelectClient("Blue"));
    }

    void OnSelectServer()
    {
        GameSettings.IsServer = true;
        GameSettings.Team = ""; // server does not have a team

        // Load a dedicated server scene if you have one.
        // For example, if your server scene is named "ServerScene":
        SceneManager.LoadScene("ServerScene");

        // If you want to use the same game scene for the server,
        // you can instead load "GameScene" or another appropriate scene.
        // SceneManager.LoadScene("GameScene");
    }

    void OnSelectClient(string team)
    {
        GameSettings.IsServer = false;
        GameSettings.Team = team;

        // Load the scene based on the selected team.
        if (team == "Red")
        {
            SceneManager.LoadScene("RedScene");
        }
        else if (team == "Blue")
        {
            SceneManager.LoadScene("BlueScene");
        }
    }
}
