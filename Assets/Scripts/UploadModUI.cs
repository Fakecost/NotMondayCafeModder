using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;
using Steamworks.Data;
using TMPro;

public class UploadModUI : MonoBehaviour
{
    public TMP_InputField bundlePathInput;
    public Button uploadButton;
    public TMP_Text statusText;

    private void Start()
    {
        statusText.text = "";
        uploadButton.onClick.AddListener(OnUploadClicked);
    }

    private async void OnUploadClicked()
    {
        string bundlePath = bundlePathInput.text.Trim();

        if (!SteamClient.IsValid)
        {
            statusText.text = "❌ Steam is not initialized.";
            return;
        }

        if (!File.Exists(bundlePath))
        {
            statusText.text = "❌ Bundle path is invalid or file not found.";
            return;
        }

        string folder = Path.GetDirectoryName(bundlePath);
        string fileName = Path.GetFileNameWithoutExtension(bundlePath);

        statusText.text = "📦 Uploading to Steam Workshop...";

        var editor = await Steamworks. Ugc.Editor.NewCommunityFile
            .WithTitle("Mod: " + fileName)
            .WithDescription("Uploaded from Unity Upload UI")
            .WithContent(folder)
            .WithTag("Mod")
            .WithTag("Character")
            .SubmitAsync();

        if (editor.Success == false)
        {
            Debug.Log("fail");
        }
    }
}