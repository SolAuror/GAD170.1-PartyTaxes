using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PartyTaxes;

public class PTAdventureLog : MonoBehaviour
{
#region Variables
    [Header("UI References")]
    public GameObject logPanel;                                                         //variable for the adventure log panel
    [SerializeField] private ScrollRect logScrollRect;                                  //variable for the scroll rect component of the adventure log panel
    [SerializeField] private RectTransform logContent;                                  //variable for the content object of the scroll rect, where log entries will be added as children
    [SerializeField] private TextMeshProUGUI logText;                                   //variable for the text component of the adventure log panel
    
    [Header("Settings")]
    [SerializeField] private int maxLogEntries = 20;
    [SerializeField] private bool useVerticalLayoutEntries = true;
    [SerializeField] private bool autoScrollWhenNearBottom = true;
    [SerializeField, Range(0.01f, 0.25f)] private float nearBottomThreshold = 0.1f;
    [SerializeField] private Color logTextColor = Color.white;
    
    [Header("Timing")]
    [SerializeField, Tooltip("Delay in seconds between log messages for readability")]
    private float logMessageDelay = 0.4f;
    private const int CATCH_UP_BACKLOG = 3;                                                              //more queued messages than this: speed up
    private const float CATCH_UP_DELAY_SCALE = 0.2f;                                                     //...to 20% of the normal delay
    private const int CATCH_UP_FLUSH_BACKLOG = 30;                                                       //more than this: print immediately
    
    private readonly List<string> logEntries = new List<string>();                         // Store raw log entries in a readonly list of strings.        -Could be used for a potential future saving/loading system. 
    private readonly List<TextMeshProUGUI> visualEntries = new List<TextMeshProUGUI>();    //store visual log entries in a readonly list of TextMeshProUGUI objects.
    private TextMeshProUGUI entryTemplate;
    private static PTAdventureLog _instance;                                               //cached singleton instance to avoid FindAnyObjectByType every call
    
    private readonly System.Collections.Generic.Queue<string> messageQueue = new System.Collections.Generic.Queue<string>();  // Queue for delayed messages
    private bool isProcessingQueue = false;                                                // Track if queue is being processed
#endregion

#region Initialization
    void Awake()                                                                          //on awake, initialize the adventure log panel before other scripts' Start() runs
    {
        _instance = this;                                                                   //cache singleton instance
        if (logScrollRect != null && logContent != null)
        {
            logScrollRect.content = logContent;
            logScrollRect.horizontal = false;
            logScrollRect.vertical = true;
        }

        if (logContent != null)                                                                 //use ContentSizeFitter component on the Content object so it grows with text
        {
            ContentSizeFitter fitter = logContent.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = logContent.gameObject.AddComponent<ContentSizeFitter>();
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        logTextColor = PTUITheme.Ink;                                                           //ink on the parchment log strip (the colours below are inks too)
        ApplyTheme();

        if (useVerticalLayoutEntries && logText != null)                                        //use the verticallayoutentries option to create a template entry for visual log entries, and disable the original text component.
        {
            entryTemplate = logText;
            entryTemplate.color = logTextColor;
            entryTemplate.gameObject.SetActive(false);
        }

        AddLogEntry("Adventure log initialized... Welcome to Party Taxes.");                   //log initialization messgae, INIT COMMIT!
    }

    //void Update(){}

    public void ApplyTheme()                                                                //lays the log out as a parchment scroll (the editor's Bake UI also calls this on the HUD prefab)
    {
        PTUIFactory.StyleLog(logScrollRect, logText);
    }
#endregion

#region Log Management Methods
    private string ColorizeMessage(string message)                             //method to automatically colorize gold and HP amounts in messages
    {
        if (string.IsNullOrEmpty(message)) return message;

        // Colourize gold amounts
        message = Regex.Replace(message, @"(\d+)\s*(gold)", "<color=" + PTUITheme.GoldInk + ">$1 $2</color>", RegexOptions.IgnoreCase);
        
        // Colourize daily wages amount
        message = Regex.Replace(message, @"(Daily Wages are\s+)(\d+)", "$1<color=" + PTUITheme.GoldInk + ">$2</color>", RegexOptions.IgnoreCase);

        // Colourize HP amounts
        message = Regex.Replace(message, @"(\d+(?:/\d+)?)\s*(HP)", "<color=" + PTUITheme.GreenInk + ">$1 $2</color>", RegexOptions.IgnoreCase);

        message = Regex.Replace(message, @"(healing|healed|heal|life|cleric)", "<color=" + PTUITheme.GreenInk + ">$1</color>", RegexOptions.IgnoreCase);

        //colourize party name markers
        message = Regex.Replace(message, @"<partyname>(.*?)</partyname>", "<color=" + PTUITheme.BlueInk + ">$1</color>");

        //colourize party member join messages
        message = Regex.Replace(message, @"^(.+?)\s+joined\s+(.+?)!$", "<color=" + PTUITheme.BlueInk + ">$1</color> joined <color=" + PTUITheme.BlueInk + ">$2</color>!", RegexOptions.IgnoreCase);

        //colourize resurrection messages
        message = Regex.Replace(message, @"\b(resurrected|resurrection|resurrect)\b", "<color=" + PTUITheme.PurpleInk + ">$1</color>", RegexOptions.IgnoreCase);

        message = Regex.Replace(message, @"\b(coward|cowardly|Cowardice|run away)\b", "<color=" + PTUITheme.BrownInk + ">$1</color>", RegexOptions.IgnoreCase);

        //colourize enemy name markers
        message = Regex.Replace(message, @"(\d+\s)?<enemy>(.*?)</enemy>(s?)", "<color=" + PTUITheme.RedInk + ">$1$2$3</color>", RegexOptions.IgnoreCase);

        //colourize death messages
        message = Regex.Replace(message, @"\bMysterious Stranger\b", "<color=" + PTUITheme.PurpleInk + ">$0</color>", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"\bMarked by Death\b", "<color=" + PTUITheme.PurpleInk + ">$0</color>", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"The Dark Ritual Begins\.\.\.", "<color=" + PTUITheme.PurpleInk + ">$0</color>", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"\b(dead|died|death)\b", "<color=" + PTUITheme.PurpleInk + ">$1</color>", RegexOptions.IgnoreCase);
        return message;
    }

    public void AddLogEntry(string message)                                    //method for adding a log entry, take a string message as input 
    {                                                                           
        if (string.IsNullOrEmpty(message)) return;

        message = ColorizeMessage(message);                                     //automatically colorize gold and HP amounts

        bool shouldAutoScroll = !autoScrollWhenNearBottom || IsNearBottom();
        
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss");               // Get current time in hours and minutes format
        string formattedEntry = $"[{timestamp}] {message}";                     // Format the log entry with the timestamp
        
        logEntries.Add(formattedEntry);                                         // Add the formatted entry to the log entries list

        if (logEntries.Count > maxLogEntries)                                   //remove oldest log entries when max is exceeded.
        {
            logEntries.RemoveAt(0);

            if (useVerticalLayoutEntries && visualEntries.Count > 0)
            {
                TextMeshProUGUI oldestEntry = visualEntries[0];
                visualEntries.RemoveAt(0);
                if (oldestEntry != null)
                {
                    Destroy(oldestEntry.gameObject);
                }
            }
        }

        if (useVerticalLayoutEntries)
        {
            AddVisualEntry(formattedEntry);                                     //add it to the visual log if using vertical layout entries component, 
            RebuildLayout(shouldAutoScroll);
        }
        else
        {
            UpdateLogDisplay(shouldAutoScroll);                                 //otherwise just update the text of the original log text component.
        }   
    }

    private void UpdateLogDisplay(bool shouldAutoScroll = true)                 //method to update the text of the original log text component with the current log entries,
    {                                                                           //       and auto scroll to the bottom if near the bottom when adding a new entry.
        if (logText == null) return;
        
        logText.text = string.Join("\n", logEntries);
        logText.color = logTextColor;
        RebuildLayout(shouldAutoScroll);
    }

    private void AddVisualEntry(string formattedEntry)                          //Method to add new visual log entries as children of the content obj,
    {                                                                           //       and inherit the properties of the template entry
        if (entryTemplate == null) return;

        TextMeshProUGUI newEntry = Instantiate(entryTemplate, logContent);
        newEntry.gameObject.SetActive(true);
        newEntry.text = formattedEntry;
        newEntry.color = logTextColor;
        visualEntries.Add(newEntry);
    }

    private void RebuildLayout(bool shouldAutoScroll)                           //Method for rebuilding the layout of the log entries, and auto scrolling to bottom if within the NearBottomThreshhold  
    {
        Canvas.ForceUpdateCanvases();

        if (logContent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(logContent);
        }

        if (shouldAutoScroll && logScrollRect != null)
        {
            logScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private bool IsNearBottom()                                                //method for calculating if the scroll rect is near the bottom, to reset scroll position on log entry addition.
    {
        if (logScrollRect == null || logContent == null)
        {
            return true;
        }

        RectTransform viewport = logScrollRect.viewport != null 
                               ? logScrollRect.viewport : logScrollRect.GetComponent<RectTransform>();
        if (viewport == null)
        {
            return true;
        }

        float scrollableHeight = logContent.rect.height - viewport.rect.height;
        if (scrollableHeight <= 1f)
        {
            return true;
        }

        return logScrollRect.verticalNormalizedPosition <= nearBottomThreshold;
    }
    public void ClearLogInstance()                                                //Instance method that performs the actual log clearing, keeps fields private
    {
        logEntries.Clear();

        for (int index = 0; index < visualEntries.Count; index++)
        {
            if (visualEntries[index] != null)
            {
                Destroy(visualEntries[index].gameObject);
            }
        }

        visualEntries.Clear();
        UpdateLogDisplay();
    }
#endregion
    public static void ClearLog()                                                //Static method for clearing the log, uses cached instance
    {
        PTAdventureLog logger = _instance != null ? _instance : FindAnyObjectByType<PTAdventureLog>();
        if (logger != null) logger.ClearLogInstance();
    }

    public static void Log(string message)                                                    //Static Method for logging messages to the adventure log, uses cached instance
    {                                                                                         
        PTAdventureLog logger = _instance != null ? _instance : FindAnyObjectByType<PTAdventureLog>();
        if (logger != null)
        {
            logger.QueueLogMessage(message);
        }
    }

    /// <summary>
    /// Queues a log message for delayed display. Messages are processed with a delay between each.
    /// </summary>
    private void QueueLogMessage(string message)
    {
        messageQueue.Enqueue(message);
        
        if (!isProcessingQueue)
        {
            StartCoroutine(ProcessMessageQueue());
        }
    }

    /// <summary>
    /// Processes queued log messages with delays between each message.
    /// </summary>
    private System.Collections.IEnumerator ProcessMessageQueue()
    {
        isProcessingQueue = true;
        
        while (messageQueue.Count > 0)
        {
            string message = messageQueue.Dequeue();
            AddLogEntry(message);
            
            // Add delay before next message if there are more in queue
            if (messageQueue.Count > CATCH_UP_FLUSH_BACKLOG)                                             //far behind (e.g. a skipped battle): show the rest at once
            {
                continue;
            }
            if (messageQueue.Count > 0 && logMessageDelay > 0)
            {
                float delay = messageQueue.Count > CATCH_UP_BACKLOG ? logMessageDelay * CATCH_UP_DELAY_SCALE : logMessageDelay; //speed up when messages pile up, so the log keeps pace with auto-combat
                yield return new WaitForSeconds(delay);
            }
        }
        
        isProcessingQueue = false;
    }
}
