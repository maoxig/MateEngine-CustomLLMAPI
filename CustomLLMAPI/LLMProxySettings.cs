using LLMUnity;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LLMProxySettings : MonoBehaviour
{
    public static LLMProxySettings Instance { get; private set; }

    public LLMProxySettingsData data;

    private string fileName = "LLMProxySettings.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, fileName);

    // Cached references
    private LLM llm;
    private LLMCharacter llmCharacter;
    private MenuActions _gameMenuActions;
    private MenuEntry _myUIMenuEntry;
    private bool _isMyUIAddedToMenuList = false;
    public KeyCode toggleKey = KeyCode.J;

    public LLMAPIProxy proxy; // Drag in Inspector or dynamically assign

    // UI references (Drag in Inspector, use TMP components)
    public Canvas targetCanvas;
    public Toggle enableToggle;
    public GameObject remoteConfigGroup;
    public TMP_Dropdown presetDropdown;
    public Button addPresetButton;
    public Button deletePresetButton;
    public TMP_Dropdown providerDropdown;
    public TMP_InputField apiKeyInput, endpointInput, modelInput, portInput;
    public TMP_Dropdown templateDropdown;
    public Button saveButton, closeButton;
    public Toggle enableHidePanelOnStartToggle;
    public TMP_Text debugText;


    private bool _isInitializing = false;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        CacheComponents();
        LoadFromDisk();
        InitializeUI();
        _isInitializing = true;
        ApplyAllSettings();
        _isInitializing = false;
        if (data.hidePanelOnStart && targetCanvas != null)
        {
            targetCanvas.gameObject.SetActive(false);
        }
        else
        {
            AddMyUIToGameMenuList();
        }
    }

    private void OnApplicationQuit()
    {
        SaveToDisk();
    }

    private void CacheComponents()
    {
        llm = FindAnyObjectByType<LLM>(FindObjectsInactive.Include);
        if (llm == null)
        {
            Debug.LogWarning("[LLMProxySettings] No LLM component found.");
        }
        llmCharacter = FindAnyObjectByType<LLMCharacter>(FindObjectsInactive.Include);
        if (llmCharacter == null)
        {
            Debug.LogWarning("[LLMProxySettings] No LLMCharacter component found.");
        }
        _gameMenuActions = FindAnyObjectByType<MenuActions>();
    }

    private void InitializeUI()
    {
        // Populate dropdowns
        providerDropdown.options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("OpenAI"),
            new TMP_Dropdown.OptionData("Anthropic"),
            new TMP_Dropdown.OptionData("Custom")
        };

        templateDropdown.options = new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("chatml"),
            new TMP_Dropdown.OptionData("alpaca"), // Add more templates as needed
            new TMP_Dropdown.OptionData("vicuna")
        };

        // Add listeners
        enableToggle.onValueChanged.AddListener(OnEnableChanged);
        enableHidePanelOnStartToggle.onValueChanged.AddListener(OnEnableHidePanelOnStartChanged);
        providerDropdown.onValueChanged.AddListener(OnProviderChanged);
        presetDropdown.onValueChanged.AddListener(OnPresetChanged);
        addPresetButton.onClick.AddListener(AddPreset);
        deletePresetButton.onClick.AddListener(DeletePreset);
        saveButton.onClick.AddListener(SaveAndApply);
        closeButton.onClick.AddListener(ClosePanel);
        portInput.onEndEdit.AddListener(OnPortChanged);
        apiKeyInput.onEndEdit.AddListener(_ => UpdateCurrentFromUI());
        endpointInput.onEndEdit.AddListener(_ => UpdateCurrentFromUI());
        modelInput.onEndEdit.AddListener(_ => UpdateCurrentFromUI());
        templateDropdown.onValueChanged.AddListener(value => {
            if (_isInitializing) return;
            UpdateCurrentFromUI();
        });

        // Initialize menu entry
        _myUIMenuEntry = new MenuEntry { menu = targetCanvas.gameObject };
    }

    public void SaveToDisk()
    {
        try
        {
            UpdateCurrentFromUI();

            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            string json = JsonConvert.SerializeObject(data, settings);
            File.WriteAllText(FilePath, json);
            Debug.Log("[LLMProxySettings] Saved to: " + FilePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LLMProxySettings] Failed to save: " + e);
        }
    }

    public void LoadFromDisk()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                data = JsonConvert.DeserializeObject<LLMProxySettingsData>(json) ?? new LLMProxySettingsData();
                Debug.Log("[LLMProxySettings] Loaded from: " + FilePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LLMProxySettings] Failed to load: " + e);
                data = new LLMProxySettingsData();
            }
        }
        else
        {
            data = new LLMProxySettingsData();
        }

        // Ensure at least one config
        if (data.apiConfigs.Count == 0)
        {
            data.apiConfigs.Add(new LLMProxySettingsData.APIConfigData { name = "Default" });
        }
    }

    public static void ApplyAllSettings()
    {
        if (Instance == null) Instance = FindAnyObjectByType<LLMProxySettings>();
        var inst = Instance;
        var d = inst.data;

        inst.enableToggle.isOn = d.enableRemote;
        inst.enableHidePanelOnStartToggle.isOn = d.hidePanelOnStart;
        inst.portInput.text = d.proxyPort.ToString();
        inst.RefreshPresetDropdown();
        inst.LoadCurrentPresetToUI();
        inst.OnEnableChanged(d.enableRemote);
        inst.ApplyConfig();
    }

    private void RefreshPresetDropdown()
    {
        presetDropdown.options = data.apiConfigs.Select(c => new TMP_Dropdown.OptionData(c.name)).ToList();
        presetDropdown.value = data.activeConfigIndex;
        presetDropdown.RefreshShownValue();
    }

    private void LoadCurrentPresetToUI()
    {
        if (data.apiConfigs.Count == 0) return;
        data.activeConfigIndex = Mathf.Clamp(data.activeConfigIndex, 0, data.apiConfigs.Count - 1);
        var conf = data.apiConfigs[data.activeConfigIndex];
        providerDropdown.value = (int)conf.provider;
        apiKeyInput.text = conf.apiKey;
        endpointInput.text = conf.apiEndpoint;
        modelInput.text = conf.model;
        templateDropdown.value = conf.templateIndex;
    }

    private void UpdateCurrentFromUI()
    {
        if (data.apiConfigs.Count == 0 || data.activeConfigIndex >= data.apiConfigs.Count) return;
        var conf = data.apiConfigs[data.activeConfigIndex];
        conf.provider = (LLMAPIProxy.APIProvider)providerDropdown.value;
        conf.apiKey = apiKeyInput.text;
        conf.apiEndpoint = endpointInput.text;
        conf.model = modelInput.text;
        conf.templateIndex = templateDropdown.value;
        conf.chatTemplate = templateDropdown.options[conf.templateIndex].text;
    }

    private void Update()
    {
        HandleKeyToggleUI();
    }

    private void OnEnableChanged(bool isEnabled)
    {
        if (_isInitializing) return;
        data.enableRemote = isEnabled;
        if (isEnabled)
        {
            if (llm != null) llm.enabled = false;
            if (llmCharacter != null)
            {
                llmCharacter.remote = true;
            }
        }
        else
        {
            if (llm != null) llm.enabled = true;
            if (llmCharacter != null)
            {
                llmCharacter.remote = false;
            }
        }
    }
    private void OnEnableHidePanelOnStartChanged(bool isEnabled)
    {
        data.hidePanelOnStart = isEnabled;
    }

    private void OnProviderChanged(int value)
    {
        if (_isInitializing) return;
        UpdateCurrentFromUI();
    }

    private void OnPresetChanged(int value)
    {
        if (_isInitializing) return;
        UpdateCurrentFromUI(); // Save previous
        data.activeConfigIndex = value;
        LoadCurrentPresetToUI();
    }

    private void AddPreset()
    {
        UpdateCurrentFromUI();
        var newConf = new LLMProxySettingsData.APIConfigData { name = $"Preset {data.apiConfigs.Count + 1}" };
        data.apiConfigs.Add(newConf);
        data.activeConfigIndex = data.apiConfigs.Count - 1;
        RefreshPresetDropdown();
        LoadCurrentPresetToUI();
    }

    private void DeletePreset()
    {
        if (data.apiConfigs.Count <= 1) return;
        data.apiConfigs.RemoveAt(data.activeConfigIndex);
        data.activeConfigIndex = Mathf.Clamp(data.activeConfigIndex, 0, data.apiConfigs.Count - 1);
        RefreshPresetDropdown();
        LoadCurrentPresetToUI();
    }

    private void OnPortChanged(string value)
    {
        if (int.TryParse(value, out int port))
        {
            data.proxyPort = port;
            if (llmCharacter != null) llmCharacter.port = port;
            if (proxy != null && proxy.isRunning)
            {
                proxy.StopProxyServer();
                proxy.proxyPort = port;

                // 修复 CS4014：使用 async void 并等待 StartProxyServer
                StartProxyServerAsync();
            }
        }
        else
        {
            Debug.LogWarning("[LLMProxySettings] Invalid port: " + value);
        }
    }

    // 新增异步方法以等待 StartProxyServer
    private async void StartProxyServerAsync()
    {
        await proxy.StartProxyServer();
    }

    private void SaveAndApply()
    {
        UpdateCurrentFromUI();
        SaveToDisk();
        ApplyConfig();
        ClosePanel();
    }

    private async void ApplyConfig()
    {
        proxy.configs = data.apiConfigs.Select(c => new LLMProxySettings.LLMProxySettingsData.APIConfigData
        {
            name = c.name,
            provider = c.provider,
            apiKey = c.apiKey,
            apiEndpoint = c.apiEndpoint,
            model = c.model,
            chatTemplate = c.chatTemplate
        }).ToList();

        proxy.currentConfigIndex = data.activeConfigIndex;
        if (proxy.proxyPort != data.proxyPort)
        {
            if (proxy.isRunning) proxy.StopProxyServer();
            proxy.proxyPort = data.proxyPort;
        }

        if (data.enableRemote)
        {
            if (!proxy.isRunning) await proxy.StartProxyServer();
            if (llm != null) llm.enabled = false;
            if (llmCharacter != null)
            {
                llmCharacter.remote = true;
                llmCharacter.port = data.proxyPort;
            }
        }
        else
        {
            proxy?.StopProxyServer();

            if (llm != null) llm.enabled = true;
            if (llmCharacter != null)
            {
                llmCharacter.remote = false;
            }
        }
    }

    private void ClosePanel()
    {
        if (targetCanvas != null)
        {
            targetCanvas.gameObject.SetActive(false);
        }
        RemoveMyUIFromGameMenuList();
    }

    private void HandleKeyToggleUI()
    {
        if (targetCanvas == null || IsInTextInputState()) return;
        if (Input.GetKeyDown(toggleKey))
        {
            bool newState = !targetCanvas.gameObject.activeSelf;
            targetCanvas.gameObject.SetActive(newState);
            if (newState) AddMyUIToGameMenuList();
            else RemoveMyUIFromGameMenuList();
        }
    }

    private bool IsInTextInputState()
    {
        // 1. First check if EventSystem exists (avoid null reference)
        if (EventSystem.current == null)
            return false;

        // 2. Get the currently selected UI object
        GameObject selectedObj = EventSystem.current.currentSelectedGameObject;
        if (selectedObj == null)
            return false;

        bool isUGUIInput = selectedObj.GetComponent<InputField>() != null;
        bool isTMPInput = selectedObj.GetComponent<TMP_InputField>() != null;

        return isUGUIInput || isTMPInput;
    }

    public void AddMyUIToGameMenuList()
    {
        if (_gameMenuActions == null || _isMyUIAddedToMenuList || _myUIMenuEntry == null) return;
        if (!_gameMenuActions.menuEntries.Exists(e => e.menu == targetCanvas.gameObject))
        {
            _gameMenuActions.menuEntries.Add(_myUIMenuEntry);
            _isMyUIAddedToMenuList = true;
        }
    }

    public void RemoveMyUIFromGameMenuList()
    {
        if (_gameMenuActions == null || !_isMyUIAddedToMenuList || _myUIMenuEntry == null) return;
        _gameMenuActions.menuEntries.RemoveAll(e => e.menu == targetCanvas.gameObject);
        _isMyUIAddedToMenuList = false;
    }

    [System.Serializable]
    public class LLMProxySettingsData
    {
        public string version = "1.0";
        public bool enableRemote = false;
        public bool hidePanelOnStart = false;
        public List<APIConfigData> apiConfigs = new List<APIConfigData>();
        public int activeConfigIndex = 0;
        public int proxyPort = 13333;

        [System.Serializable]
        public class APIConfigData
        {
            public string name = "Default";
            public LLMAPIProxy.APIProvider provider = LLMAPIProxy.APIProvider.OpenAI;
            public string apiKey = "";
            public string apiEndpoint = "https://api.openai.com/v1/chat/completions";
            public string model = "gpt-3.5-turbo";
            public int templateIndex = 0;
            public string chatTemplate = "chatml";
        }
    }
}
