using LLMUnity;
using Newtonsoft.Json;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class LLMProxySettings : MonoBehaviour
{
    public static LLMProxySettings Instance { get; private set; }

    public LLMProxySettingsData data;

    private string fileName = "LLMProxySettings.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, fileName);

    // Cached references for efficient apply/save
    private LLM llm; // 本地LLM
    private LLMCharacter llmCharacter;

    private MenuActions _gameMenuActions; // Game's existing MenuActions instance
    private MenuEntry _myUIMenuEntry; // Menu entry for this UI
    private bool _isMyUIAddedToMenuList = false; // Track if UI is in menu list
    public KeyCode toggleKey = KeyCode.J; // 绑定到J键

    public LLMAPIProxy proxy; // Reference to the LLMAPIProxy component (在Inspector中拖拽或动态创建)

    // UI引用（在Inspector拖拽，使用TMP组件）
    public Canvas targetCanvas; // UI Canvas
    public Toggle enableToggle;
    public GameObject remoteConfigGroup;
    public TMP_Dropdown providerDropdown; // 使用TMP_Dropdown
    public TMP_InputField apiKeyInput, endpointInput, modelInput, portInput; // 使用TMP_InputField
    public TMP_Dropdown templateDropdown; // 使用TMP_Dropdown
    public Button saveButton, closeButton;

    public TMP_Text debugText; // 用于显示调试信息

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

    }

    private void Start()
    {
        // Cache references
        CacheComponents();

        LoadFromDisk();
        ApplyAllSettings();

        // Initialize menu entry
        _myUIMenuEntry = new MenuEntry
        {
            menu = targetCanvas.gameObject,
        };

        // 初始化UI事件
        enableToggle.onValueChanged.AddListener(OnEnableChanged);
        providerDropdown.onValueChanged.AddListener(OnProviderChanged);
        saveButton.onClick.AddListener(SaveAndApply);
        closeButton.onClick.AddListener(ClosePanel);
        portInput.onEndEdit.AddListener(OnPortChanged);

    }

    private void OnApplicationQuit()
    {
        SaveToDisk(); // Auto-save on exit
    }

    private void CacheComponents()
    {
        llm = FindFirstObjectByType<LLM>(UnityEngine.FindObjectsInactive.Include);
        if (llm == null)
        {
            Debug.LogWarning("[LLMProxySettings] No LLM component found in the scene.");
        }
        llmCharacter = FindFirstObjectByType<LLMCharacter>(UnityEngine.FindObjectsInactive.Include);
        if (llmCharacter == null)
        {
            Debug.LogWarning(
            "[LLMProxySettings] No LLMCharacter component found in the scene. Remote LLM may not function correctly."
            );
        }
        _gameMenuActions = FindFirstObjectByType<MenuActions>(); // 查找游戏的MenuActions
    }

    public void SaveToDisk()
    {
        try
        {
            // Sync data from UI before saving
            SyncDataFromUI();

            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var settings = new JsonSerializerSettings
            {
                Formatting = Newtonsoft.Json.Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            string json = JsonConvert.SerializeObject(data, settings);
            File.WriteAllText(FilePath, json);
            Debug.Log("[LLMProxySettings] Saved settings to: " + FilePath);
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
                data = JsonConvert.DeserializeObject<LLMProxySettingsData>(json);
                Debug.Log("[LLMProxySettings] Loaded settings from: " + FilePath);
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
    }

    public static void ApplyAllSettings()
    {
        if (Instance == null)
        {
            Instance = FindFirstObjectByType<LLMProxySettings>();
        }
        var data = Instance.data;

        // Apply to UI (for display)
        Instance.enableToggle.isOn = data.enableRemote;
        Instance.providerDropdown.value = (int)data.provider;
        Instance.apiKeyInput.text = data.apiKey;
        Instance.endpointInput.text = data.apiEndpoint;
        Instance.modelInput.text = data.model;
        Instance.templateDropdown.value = data.templateIndex;
        Instance.portInput.text = data.proxyPort.ToString();

        Instance.OnEnableChanged(data.enableRemote);
        Instance.OnProviderChanged((int)data.provider);
        Instance.OnPortChanged(data.proxyPort.ToString());

        // Apply to scene
        Instance.ApplyConfig();
    }

    private void SyncDataFromUI()
    {
        data.enableRemote = enableToggle.isOn;
        if (data.enableRemote)
        {
            data.provider = (LLMAPIProxy.APIProvider)providerDropdown.value;
            data.apiKey = apiKeyInput.text;
            data.apiEndpoint = endpointInput.text;
            data.model = modelInput.text;
            data.templateIndex = templateDropdown.value;
            data.chatTemplate = templateDropdown.options[templateDropdown.value].text;
            if (int.TryParse(portInput.text, out int port))
            {
                data.proxyPort = port;
            }
        }
    }

    private void Update()
    {
        HandleKeyToggleUI(); // 处理J键开关
    }

    private void OnEnableChanged(bool isEnabled)
    {
        remoteConfigGroup.SetActive(isEnabled);
        if (isEnabled)
        {
            llm.enabled = false;
            llmCharacter.remote = true;
            llmCharacter.stream = false;

        }
        else
        {
            llm.enabled = true;
            llmCharacter.remote = false;
            llmCharacter.stream = true;

        }
    }

    private void OnProviderChanged(int value)
    {
        LLMAPIProxy.APIProvider provider = (LLMAPIProxy.APIProvider)value;

    }

    private void OnPortChanged(string value)
    {
        if (int.TryParse(value, out int port))
        {
            data.proxyPort = port;
            llmCharacter.port = port;
        }
        else
        {
            Debug.LogWarning("[LLMProxySettings] Invalid port number: " + value);
        }
    }

    private void SaveAndApply()
    {
        SaveToDisk();
        ApplyConfig();
        ClosePanel();
    }

    private void ApplyConfig()
    {

        if (data.enableRemote)
        {
            proxy.provider = data.provider;
            proxy.apiKey = data.apiKey;
            proxy.apiEndpoint = data.apiEndpoint;
            proxy.model = data.model;
            proxy.chatTemplate = data.chatTemplate;
            proxy.proxyPort = data.proxyPort;

            // 修改场景
            if (llm != null)
            {
                llm.enabled = false;
            }
            if (llmCharacter != null)
            {
                llmCharacter.remote = true;
                llmCharacter.port = data.proxyPort; // 假设LLMCharacter有public int port
            }
        }
        else
        {
            if (proxy != null)
            {
                proxy.StopProxyServer();
                Destroy(proxy.gameObject);
                proxy = null;
            }

            // 恢复本地
            if (llm != null)
            {
                llm.enabled = true;
            }
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
        RemoveMyUIFromGameMenuList(); // 关闭时从菜单移除
    }

    private void HandleKeyToggleUI()
    {
        if (targetCanvas == null) return;
        if (IsInTextInputState()) return; // 防止输入时触发
        if (Input.GetKeyDown(toggleKey))
        {
            GameObject targetCanvasObject = targetCanvas.gameObject;
            bool newVisibleState = !targetCanvasObject.activeSelf;
            targetCanvasObject.SetActive(newVisibleState);

            if (newVisibleState)
            {
                AddMyUIToGameMenuList(); // 打开时添加到菜单
            }
            else
            {
                RemoveMyUIFromGameMenuList(); // 关闭时移除
            }
        }
    }

    private bool IsInTextInputState()
    {
        return apiKeyInput.isFocused || endpointInput.isFocused || modelInput.isFocused || portInput.isFocused;
    }

    public void AddMyUIToGameMenuList()
    {
        if (_gameMenuActions == null || _isMyUIAddedToMenuList || _myUIMenuEntry == null)
            return;

        bool isAlreadyInList = _gameMenuActions.menuEntries.Exists(
            entry => entry.menu == targetCanvas.gameObject
        );
        if (!isAlreadyInList)
        {
            _gameMenuActions.menuEntries.Add(_myUIMenuEntry);
            _isMyUIAddedToMenuList = true;
        }
    }

    public void RemoveMyUIFromGameMenuList()
    {
        if (_gameMenuActions == null || !_isMyUIAddedToMenuList || _myUIMenuEntry == null)
            return;

        _gameMenuActions.menuEntries.RemoveAll(
            entry => entry.menu == targetCanvas.gameObject
        );
        _isMyUIAddedToMenuList = false;
    }

    // Public method to trigger save on setting changes (call from other scripts if needed)
    public static void OnSettingChanged()
    {
        if (Instance != null)
        {
            Instance.SaveToDisk();
        }
    }

    [System.Serializable]
    public class LLMProxySettingsData
    {
        public string version = "1.0"; // For future migrations
        public bool enableRemote = false;
        public LLMAPIProxy.APIProvider provider = LLMAPIProxy.APIProvider.OpenAI;
        public string apiKey = "";
        public string apiEndpoint = "https://api.openai.com/v1/chat/completions";
        public string model = "gpt-3.5-turbo";
        public int templateIndex = 0; // Index for dropdown
        public string chatTemplate = "chatml";
        public bool debug = false;
        public int proxyPort = 13333;
    }
}