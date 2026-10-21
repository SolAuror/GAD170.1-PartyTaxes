using System.Collections.Generic;                                                                            //use System.Collections.Generic - for lists
using UnityEngine;                                                                                           //use UnityEngine for MonoBehaviour and other Unity features
using UnityEngine.InputSystem;                                                                               //use UnityEngine.InputSystem for the new Input System
using UnityEngine.EventSystems;                                                                              //use UnityEngine.EventSystems to check if the pointer is over the UI
using UnityEngine.UI;                                                                                        //use UnityEngine.UI for Slider references
using UnityEngine.SceneManagement;                                                                          //use UnityEngine.SceneManagement for reloading the scene on reset
using TMPro;                                                                                                 //use TMPro for TextMeshPro references
using PartyTaxes;                                                                                            //use the PartyTaxes namespace to access the PTSoul class

[System.Serializable]
public class DeadCharacterData                                                                           //class to store information about dead characters for potential resurrection
{
    public string name;
    public string type;
    public int level;
    public int currentXp;
    public int dailywages;
    public int baseWage;
    public PTEmployeeRecord employment;                                                                  //their employment file (loyalty, greed, history) comes back with them
    public int atrMight;                                                                                 //attributes are stored instead of derived stats, so they can be recalculated on resurrection
    public int atrAgility;
    public int atrConstitution;
    public int atrSense;
    public int atrLuck;
    public int levelBonusHP;
    public float statMultiplier;
    public SoulAttribute levelUpAttribute;
    public GameObject originalPrefab;

    public DeadCharacterData(PTSoul soul, GameObject prefab)                                             //constructor to copy data from a PTSoul
    {
        name = soul.Name;
        type = soul.Type;
        level = soul.level;
        currentXp = soul.currentXp;
        dailywages = soul.dailywages;
        baseWage = soul.baseWage;
        employment = soul.employment;
        atrMight = soul.atrMight;
        atrAgility = soul.atrAgility;
        atrConstitution = soul.atrConstitution;
        atrSense = soul.atrSense;
        atrLuck = soul.atrLuck;
        levelBonusHP = soul.levelBonusHP;
        statMultiplier = soul.statMultiplier;
        levelUpAttribute = soul.levelUpAttribute;
        originalPrefab = prefab;
    }

    public void ApplyTo(PTSoul soul, float statPenalty = 0.9f)                                           //restore character data onto a PTSoul with optional stat penalty
    {
        soul.Name = name;
        soul.Type = type;
        soul.level = level;
        soul.currentXp = currentXp;
        soul.dailywages = dailywages;
        soul.baseWage = baseWage;
        soul.employment = employment;
        soul.atrMight = atrMight;
        soul.atrAgility = atrAgility;
        soul.atrConstitution = atrConstitution;
        soul.atrSense = atrSense;
        soul.atrLuck = atrLuck;
        soul.levelBonusHP = levelBonusHP;
        soul.levelUpAttribute = levelUpAttribute;
        soul.statMultiplier = statMultiplier * statPenalty;                                              //apply stat penalty, it stays on the soul so it survives level ups
        soul.isAdversary = false;
        soul.markedByDeath = true;                                                                       //mark as resurrected
        soul.isCowardly = false;                                                                         //resurrected souls are fearless
        soul.FinishSetup();                                                                              //recalculate stats, resurrect at full HP, and stop Start() overwriting this data
    }
}

public partial class PTManager : MonoBehaviour, SimpleControls.IPlayerActions                            //inherit from MonoBehaviour and implement the IPlayerActions interface from the SimpleControls input actions
{
    public enum GameState { Setup, Town, Combat, GameOver, Victory }                                     //what the game is doing right now, decides which actions are allowed
#region Variables
  public bool debugMode = false;                                                                             //bool for debug toggling 

  [Header("Character & Enemy Prefabs")]                                                                      //header for character prefabs in the inspector
  [Tooltip("Base prefab used to instantiate party members. Must have a PTSoul component.")]
  public GameObject partyBasePrefab;                                                                         //base prefab for spawning party members (randomized by PTSoulGen)
  public int startingPartySize = 2;                                                                          //number of random party members to spawn at game start
  public GameObject[] enemyPrefabs;                                                                           //array of enemy prefabs to randomly spawn from
  public Transform partySpawnPoint;                                                                          //transform for party spawn location
  public Transform enemySpawnPoint;                                                                          //transform for enemy spawn location

  [Header("Party & Combat")]                                                                                 //header for party and combat settings in the inspector                           
  public List<PTSoul> partyMembers = new List<PTSoul>();                                                     //list to store the party members, using the PTSoul class for an individuals information.
  public List<PTSoul> adversaries = new List<PTSoul>();                                                      //list to store the enemies, using the PTSoul class for an individuals information.
  public int gold = 90;                                                                                      //integer # of gold
  public static event System.Action<int> OnGoldChanged;                                                     //event fired whenever gold changes, passes the new gold value
  public int totalWages;                                                                                     //integer # for total calculated wages, party member count x character dailywages.
  public int daysElapsed = 0;                                                                                //integer # of days elapsed       
  public int partyCount => partyMembers.Count;                                                               //derived from list - always in sync
  public int enemyCount => adversaries.Count;                                                                //derived from list - always in sync
  public int maxPartyCount = 4;                                                                              //integer # of max party members
  public AudioClip BGMusic;                                                                                  //AudioClip for background music
  public AudioClip goldSound;                                                                                //AudioClip for gold sound effect
  public GameObject Sun;                                                                                     //Reference to the sun onject for changing instensity when sleeping
    public string partyName = "The Adventurers";                                                         //variable string for the party name
    [Tooltip("Build the clickable action bar on start. Turn off if you replace it with your own UI.")]
    public bool buildActionBar = true;
#endregion

#region UI References and Messages
  [Header("UI References")]                                                                                 //header for UI references in the inspector
  public TextMeshProUGUI battleText;                                                                        //reference to TextMeshPro level text obj
  public TextMeshProUGUI goldText;                                                                          //reference to TextMeshPro gold text obj
  public TextMeshProUGUI dayText;                                                                           //reference to TextMeshPro day text obj
  public TextMeshProUGUI PartyNameText;                                                                     //reference to TextMeshPro party name text obj
  public TextMeshProUGUI controlsText;                                                                    //reference to TextMeshPro combat log text obj
  public string dailyMsg;                                                                                    //variable string for the daily message
  public string enemyMsg;                                                                                    //variable string for the enemy message
  public string[] sleepMsgs = {                                                                              //string array for possible messages
    "You sleep roughly through the night and wake up feeling restless.",
    "You sleep soundly through this night and wake up feeling refreshed.",
    "You don't sleep at all this night, you feel lethargic and worn out."
  };                                                                                                         // possible sleep messages

    public GameState State { get; private set; } = GameState.Town;                                       //current game state, replaces the old canSleep/gameOver flags
    public bool IsRunOver => State == GameState.GameOver || State == GameState.Victory;                  //lost or won, either way no more actions
  public bool hasFought = false;                                                                             //bool to check if the player has fought
  public bool hasHealed = false;                                                                             //bool to check if the player has healed
  public bool hasBeenBlessed = false;
  

  private SimpleControls controls;                                                                           //reference to the input controls
  private AudioSource audioSource;                                                                           //reference to the AudioSource component
  private List<DeadCharacterData> deadCharacters = new List<DeadCharacterData>();                           //stores full data of dead characters for resurrection
    private PTSoulGen soulGen;                                                                           //reference to the soul generator for spawning recruits and enemies
    private PTActionBar actionBar;                                                                       //clickable buttons for every action, built at runtime
    private Image battleBanner;                                                                          //the cloth banner behind battleText, red in battle and blue in town
    private bool pointerOverUI = false;                                                                  //true while the mouse/touch is over a UI element, so UI clicks don't also count as attacks

//constants for game balance and tuning
  private const int BASE_HEAL_AMOUNT = 50;                                                                   //base HP restored per party member
  private const int HEAL_COST_PER_MEMBER = 10;                                                               //gold cost per party member for healing
  private const int BLESS_COST_PER_MEMBER = 100;                                                             //gold cost per party member for blessing
  private const int BASE_RESURRECTION_COST = 100;                                                            //base gold cost for resurrection
  private const float FLEE_COST_PERCENT = 0.1f;                                                              //10% of gold to flee
  private const int RESURRECTION_UNLOCK_DAY = 7;                                                             //day when resurrection becomes available
    private const float REST_HEAL_PERCENT = 0.25f;                                                       //share of max HP recovered overnight when wages are paid
    private const int ESCALATION_START_DAY = 8;                                                          //day the extra enemy escalation begins (week 2), for jobs that escalate
    private const int ESCALATION_EVERY_DAYS = 7;                                                         //one more potential enemy each week after escalation starts
#endregion

#region Initilization and Update
    void Awake()                                                                                             //On Awake, called once before start.   
    {
        controls = new SimpleControls();                                                                     //initialize controls in Awake (ScriptableObject creation is not allowed in field initializers)
        controls.Player.SetCallbacks(this);                                                                  //register input callbacks
    }

    void Start()                                                                                             //On Game Start, called once before update
    {   
        // Set up background music
        audioSource = GetComponent<AudioSource>();                                                           //get the AudioSource component
        if (BGMusic != null)                                                                                 //if BGMusic clip is assigned
        {
            audioSource.clip = BGMusic;                                                                      //set the audio clip
            audioSource.loop = true;                                                                         //enable looping
            audioSource.Play();                                                                              //play the music
        }

        soulGen = GetComponent<PTSoulGen>();                                                             //get the soul generator component
        StyleHud();                                                                                      //paper labels for the HUD prefab's texts
        if (buildActionBar)
        {
            actionBar = PTActionBar.Create(this);                                                        //build the clickable action buttons
        }
        State = GameState.Setup;                                                                         //choose an accusation, Audit Level and seed first
        if (actionBar == null)
        {
            BeginRun(Accusation.Clerk, 1, PTRunRules.RandomSeed(), false);                          //no setup screen: start a standard run straight away
        }
        else
        {
            UpdateUI();                                                                                  //the action bar shows the run setup screen
        }

        if (debugMode)                                                                                      //if debug mode is enabled, print debug message
        {
            Debug.Log("Debug Mode Enabled");
        }
    }

    void Update()                                                                                        //On Update
    {
        pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();    //cached here, it can't be queried reliably from inside input callbacks
    }

    void OnEnable()                                                                                         //Method for enabling the input controls
    {
        controls?.Player.Enable();    //NOTE TO SELF: null conditional to avoid errors if controls is not assigned for some reason
    }

    void OnDisable()                                                                                        //Method for disabling the input controls
    {
        controls?.Player.Disable();
    }
#endregion

#region Shared Helpers

    int CalculateTotalDailyWages()                                                                          //Integer Method to calculate total daily wages from all party members
    {
        totalWages = 0;                                                                                     //initialize total wages
        foreach (PTSoul member in partyMembers)                                                             //loop through all party members
        {
            totalWages += member.dailywages;                                                                //add each member's daily wages to total
        }
        return totalWages;                                                                                  //return total wages
    }

    void UpdateUI()                                                                                         //Method to update UI elements
    {
        if (goldText != null)
        {
            goldText.text = "<b>" + gold + "</b> <size=70%>gold</size>";                                     //display current gold (next to the coin)
            goldText.color = (gold < PayrollTonight || UnpaidLastNight > 0) ? PTUITheme.InkRed : PTUITheme.Ink; //red ink when you can't cover tonight's payroll or someone went unpaid
        }
        
        if (battleText != null)
        {
            if (adversaries.Count > 0)                                                                      //if in battle
            {
                battleText.text = "In battle with " + adversaries.Count + " enemies";           //display enemy count
                if (battleBanner != null) battleBanner.color = PTUITheme.BannerWar;             //war banner
            }
            else                                                                                            //if not in battle
            {
                battleText.text = "In Town";                                                   //display in town message
                if (battleBanner != null) battleBanner.color = PTUITheme.BannerTown;
            }
        }

        if (dayText != null)
        {
            dayText.text = "<size=70%>Days served</size>\n" + (daysElapsed > 0 ? PTUITheme.Tally(daysElapsed) : "<i>none yet</i>"); //tally marks, like a prisoner's wall
        }

        if (PartyNameText != null)
        {
            PartyNameText.text = partyName;                                                                   //display party name
        }

        UpdateControlsDisplay();                                                                         //update contextual controls display
        if (actionBar != null)
        {
            actionBar.Refresh();                                                                         //update action button labels, costs and availability
        }
    }

    void StyleHud()                                                                                      //Method to turn the HUD prefab's texts into things in the world (see PTUIFactory.StyleHud)
    {
        Canvas hud = goldText != null ? goldText.canvas : null;
        battleBanner = PTUIFactory.StyleHud(hud != null ? hud.rootCanvas.transform : null);              //does nothing to a HUD that was baked in the editor
    }

    void UpdateControlsDisplay()                                                                         //Method to update controls display based on context
    {
        if (controlsText == null) return;                                                                //exit if controls text reference not assigned

        string controls = "";

        if (State == GameState.Combat)                                                                   //in battle - combat plays itself, these are the interventions
        {
            controls = "<b>COMBAT</b> (" + stance + ")\n";
            controls += "1 - Hazard Pay\n";
            controls += "Q - Potion\n";
            controls += "3 - Bribe\n";
            controls += "F - Focus Fire\n";
            if (CanRetreat) controls += "R - Retreat\n";
            controls += "Spacebar - Speed (" + Speed + ")\n";
        }
        else                                                                                             //in town - show town controls
        {
            controls = "<b>TOWN</b>\n";
            controls += "E - Job Board\n";
            controls += "Spacebar - Sleep\n";
            controls += "F - Tavern (hire)\n";
            if (!hasHealed)                                                                              //only show heal option if the player has not healed yet today
            {
                controls += "Q - Heal Party\n";
            }
            if (!hasBeenBlessed)                                                                         //only show bless option if the player has not blessed yet today
            {
                controls += "1 - Bless Party\n";
            }
            if (daysElapsed >= RESURRECTION_UNLOCK_DAY)                                                  //only show resurrect after it unlocks on day 7
            {
                controls += "3 - Resurrect\n";
            }
        }

        controlsText.text = controls;                                                                    //update the controls text
    }

    void ChangeGold(int amount)                                                                             //Method to change gold amount, takes an integer amount as a parameter (positive or negative)
    {
        if (amount == 0)
        {
            return;
        }

        gold += amount;
        gold = Mathf.Max(gold, 0);                                                                         //clamp gold so it never goes negative
        OnGoldChanged?.Invoke(gold);                                                                       //notify all listeners of the new gold value

        if (goldSound != null)
        {
            audioSource.PlayOneShot(goldSound);                                                             //play gold sound effect when gold changes
        }

        UpdateUI();                                                                                         //automatically update UI whenever gold changes
    }

    void TriggerGameOver(string message, string outcome = "Game Over")                                   //Centralized method to trigger game over state
    {
        PTAdventureLog.Log(message + " Game Over.");
        RunOutcome = outcome;
        State = GameState.GameOver;
        RecordRunEnd(false);                                                                             //save progress between runs
        UpdateUI();
    }

    void SetSunIntensity(float intensity)                                                                   //Method to change the intensity of the sun on sleep 
    {
        if (Sun == null)
        {
            return;
        }

        Light sunLight = Sun.GetComponent<Light>();                                                         //get sun's light component

        if (sunLight != null)
        {
            sunLight.intensity = intensity;                                                                 //set sun light intensity                   
        }
    }
#endregion

    public void On_2(InputAction.CallbackContext context)
    {
        return;
    }
    
    public void On_4(InputAction.CallbackContext context)                                                //Debug method - grant the party 100 XP each, for testing level ups
    {
        if (!context.performed) return;
        if (!debugMode)
        {
            PTAdventureLog.Log("Debug mode is not enabled. Press 4 only works when Debug Mode is enabled in PTManager.");
            return;
        }
        DebugGrantPartyXP(100);
    }

    public void On_5(InputAction.CallbackContext context)
    {
        return;
    }
}