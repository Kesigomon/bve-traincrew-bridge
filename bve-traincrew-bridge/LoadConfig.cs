using IniParser;
using IniParser.Model;

namespace bve_traincrew_bridge;

public class TASCConfigObject
{
    public int tascenabled { get; set; }
    public int tascmonitor { get; set; }
    public int tascbrake { get; set; }
    public int tascposition { get; set; }
    public int inching { get; set; }
}

public static class Config
{
    public static bool ApiEnable { get; set; } = false;
    public static int RestApiPort { get; set; } = 56001;
    public static TASCConfigObject TASCConfig { get; set; } = new();
    
    public static void LoadConfig()
    {
        var parser = new FileIniDataParser();
        IniData appConfig = new();
        
        // Bridge config
        try
        {
            appConfig = parser.ReadFile("bridge_config.ini");
        }
        catch (Exception)
        {
            Console.WriteLine("設定ファイルが見つかりませんでした。デフォルト設定で起動します。");
        }
        
        const string tanudenModApiSectionName = "tanuden_mod_api";
        // Only if the key exists, set it
        if (appConfig[tanudenModApiSectionName].ContainsKey("enable"))
        {
            ApiEnable = bool.Parse(appConfig[tanudenModApiSectionName]["enable"]);
        }
        
        
        // Set for autopilot
        const string panelSectionName = "autopilot_panel";
        
        if (appConfig[panelSectionName].ContainsKey("tascenabled"))
        {
            TASCConfig.tascenabled = int.Parse(appConfig[panelSectionName]["tascenabled"]);
        }
        if (appConfig[panelSectionName].ContainsKey("tascmonitor"))
        {
            TASCConfig.tascmonitor = int.Parse(appConfig[panelSectionName]["tascmonitor"]);
        }
        if (appConfig[panelSectionName].ContainsKey("tascbrake"))
        {
            TASCConfig.tascbrake = int.Parse(appConfig[panelSectionName]["tascbrake"]);
        }
        if (appConfig[panelSectionName].ContainsKey("tascposition"))
        {
            TASCConfig.tascposition = int.Parse(appConfig[panelSectionName]["tascposition"]);
        }
        if (appConfig[panelSectionName].ContainsKey("inching"))
        {
            TASCConfig.inching = int.Parse(appConfig[panelSectionName]["inching"]);
        }
    }
}