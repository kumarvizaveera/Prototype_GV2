using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections.Generic;
using System.Text;

namespace GV.DebugTools
{
    public class DebugConsole : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private Key toggleKey = Key.Backquote;
        [SerializeField] private int maxLogLines = 100;
        [SerializeField] private float fontSize = 24f; // Increased for better readability on high DPI screens
        
        [Header("References (Auto-generated if null)")]
        [SerializeField] private Canvas consoleCanvas;
        [SerializeField] private TMP_Text logText;
        [SerializeField] private TMP_InputField commandInput;
        [SerializeField] private ScrollRect scrollRect;

        private bool isVisible = false;
        private StringBuilder logBuffer = new StringBuilder();
        private List<string> logLines = new List<string>();
        
        // Command history
        private List<string> commandHistory = new List<string>();
        private int historyIndex = 0;

        private static DebugConsole instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Auto-generate UI if missing
            if (consoleCanvas == null)
            {
                CreateConsoleUI();
            }

            // Hide initially
            SetVisible(false);
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void Update()
        {
            // Toggle visibility
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            {
                SetVisible(!isVisible);
            }

            // Command history navigation
            if (isVisible && commandInput != null && commandInput.isFocused && commandHistory.Count > 0)
            {
                if (Keyboard.current.upArrowKey.wasPressedThisFrame)
                {
                    historyIndex = Mathf.Max(0, historyIndex - 1);
                    if (historyIndex < commandHistory.Count)
                    {
                        commandInput.text = commandHistory[historyIndex];
                        commandInput.caretPosition = commandInput.text.Length;
                    }
                }
                else if (Keyboard.current.downArrowKey.wasPressedThisFrame)
                {
                    historyIndex = Mathf.Min(commandHistory.Count, historyIndex + 1);
                    if (historyIndex < commandHistory.Count)
                    {
                        commandInput.text = commandHistory[historyIndex];
                        commandInput.caretPosition = commandInput.text.Length;
                    }
                    else
                    {
                        commandInput.text = "";
                    }
                }
            }
        }

        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            string color = "white";
            string prefix = "";

            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    color = "#FF4444"; // Red
                    prefix = "[ERR] ";
                    // For errors, include stack trace (truncated)
                    logString += $"\n<size=80%>{stackTrace}</size>";
                    break;
                case LogType.Warning:
                    color = "yellow";
                    prefix = "[WARN] ";
                    break;
                case LogType.Log:
                    color = "white";
                    prefix = "[INFO] ";
                    break;
            }

            string formattedLog = $"<color={color}>{prefix}{logString}</color>";
            
            logLines.Add(formattedLog);
            if (logLines.Count > maxLogLines)
            {
                logLines.RemoveAt(0);
            }

            UpdateLogDisplay();
        }

        private void UpdateLogDisplay()
        {
            if (logText == null) return;

            logBuffer.Clear();
            foreach (string line in logLines)
            {
                logBuffer.AppendLine(line);
            }
            logText.text = logBuffer.ToString();

            // Scroll to bottom
            if (scrollRect != null && isVisible)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;
            if (consoleCanvas != null)
            {
                consoleCanvas.gameObject.SetActive(visible);
            }

            if (visible)
            {
                if (commandInput != null)
                {
                    commandInput.ActivateInputField();
                    commandInput.Select();
                }
                
                // Refresh scroll position
                UpdateLogDisplay();
            }
        }

        public void ExecuteCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            // Add to history
            commandHistory.Add(command);
            historyIndex = commandHistory.Count;
            
            // Echo command
            HandleLog($"> {command}", "", LogType.Log);

            // Simple parser
            string[] parts = command.Trim().Split(' ');
            string cmd = parts[0].ToLower();
            
            switch (cmd)
            {
                case "help":
                    HandleLog("Available commands:", "", LogType.Log);
                    HandleLog("  help - Show this list", "", LogType.Log);
                    HandleLog("  clear - Clear console logs", "", LogType.Log);
                    HandleLog("  quit - Quit application", "", LogType.Log);
                    HandleLog("  reload - Reload current scene", "", LogType.Log);
                    HandleLog("  fps - Toggle FPS display (not impl)", "", LogType.Log);
                    break;
                    
                case "clear":
                    logLines.Clear();
                    UpdateLogDisplay();
                    break;
                    
                case "quit":
                    Application.Quit();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                    break;
                    
                case "reload":
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                    break;

                case "test_error":
                    Debug.LogError("This is a test error from the Debug Console!");
                    break;

                case "test_log":
                    Debug.Log($"Test log at {Time.time}");
                    break;
                    
                default:
                    HandleLog($"Unknown command: {cmd}", "", LogType.Warning);
                    break;
            }

            // Clear input
            if (commandInput != null)
            {
                commandInput.text = "";
                commandInput.ActivateInputField();
            }
        }

        // --- UI Generation ---

        private void CreateConsoleUI()
        {
            // Verify EventSystem exists
            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // 1. Create Canvas
            GameObject canvasGO = new GameObject("DebugConsole_Canvas");
            canvasGO.transform.SetParent(this.transform);
            consoleCanvas = canvasGO.AddComponent<Canvas>();
            consoleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            consoleCanvas.sortingOrder = 9999;
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasGO.AddComponent<GraphicRaycaster>();

            // 2. Create Panel
            GameObject panelGO = new GameObject("Console_Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            Image panelImage = panelGO.AddComponent<Image>();
            panelImage.color = new Color(0, 0, 0, 0.8f);
            RectTransform panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero; // Full screen
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // 3. Create Input Field (Bottom)
            GameObject inputGO = new GameObject("Console_Input");
            inputGO.transform.SetParent(panelGO.transform, false);
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            
            commandInput = inputGO.AddComponent<TMP_InputField>();
            RectTransform inputRect = inputGO.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0, 0);
            inputRect.anchorMax = new Vector2(1, 0);
            inputRect.pivot = new Vector2(0.5f, 0);
            inputRect.sizeDelta = new Vector2(0, 50); // Height 50
            inputRect.anchoredPosition = Vector2.zero;

            // Input Text Area
            GameObject inputTextAreaGO = new GameObject("Text Area");
            inputTextAreaGO.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRect = inputTextAreaGO.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10, 0);
            textAreaRect.offsetMax = new Vector2(-10, 0);

            GameObject inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(inputTextAreaGO.transform, false);
            TMP_Text inputText = inputTextGO.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = fontSize;
            inputText.color = Color.white;
            commandInput.textComponent = inputText;
            commandInput.textViewport = textAreaRect;

            // Fix Input Field selection
            commandInput.onSubmit.AddListener(ExecuteCommand);

            // 4. Create Scroll View (Rest of space)
            GameObject scrollGO = new GameObject("Console_Scroll");
            scrollGO.transform.SetParent(panelGO.transform, false);
            scrollRect = scrollGO.AddComponent<ScrollRect>();
            RectTransform scrollRectTrans = scrollGO.GetComponent<RectTransform>();
            scrollRectTrans.anchorMin = new Vector2(0, 0);
            scrollRectTrans.anchorMax = new Vector2(1, 1);
            scrollRectTrans.offsetMin = new Vector2(0, 50); // Above input
            scrollRectTrans.offsetMax = Vector2.zero;

            // Viewport
            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            RectTransform viewportRect = viewportGO.GetComponent<RectTransform>();
            if (viewportRect == null) viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            scrollRect.viewport = viewportRect;

            // Content
            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 0);
            scrollRect.content = contentRect;

            VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 5. Log Text (Inside Content)
            GameObject textGO = new GameObject("LogText");
            textGO.transform.SetParent(contentGO.transform, false);
            logText = textGO.AddComponent<TextMeshProUGUI>();
            logText.fontSize = fontSize;
            logText.color = Color.white;
            logText.alignment = TextAlignmentOptions.BottomLeft;
            logText.enableWordWrapping = true;
            
            // Assign font if default is missing (This can be tricky at runtime without resources, but TMP usually has default)
            // If the user has a specific font, they can assign it in the inspector later.
        }
    }
}
