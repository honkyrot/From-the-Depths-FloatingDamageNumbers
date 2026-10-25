using System;
using System.IO;
using System.Reflection;
using SystemVersion = System.Version;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.Modding;
using BrilliantSkies.Ui.Displayer;
using BrilliantSkies.Ui.Displayer.Types;
using Newtonsoft.Json.Linq;
using Steamworks;

internal static class ModInformation
{
    private static string? _name;

    private static string? _myModDirPath;

    private static SystemVersion _modVersion = new(0, 0, 0);

    private static ulong _workshopID;

    private static int _requestCount;

    private static CallResult<SteamUGCRequestUGCDetailsResult_t>? _steamCall;

    public static string MyModFolderPath
    {
        get
        {
            if (string.IsNullOrEmpty(_myModDirPath))
            {
                Preparation();
            }
            return _myModDirPath ?? "";
        }
    }

    public static string MyModName
    {
        get
        {
            if (string.IsNullOrEmpty(_name))
            {
                Preparation();
            }
            return _name ?? "";
        }
    }

    private static void Preparation()
    {
        string text = Assembly.GetExecutingAssembly().Location;
        string directoryName = Path.GetDirectoryName(text);
        while (!string.IsNullOrEmpty(directoryName) && Path.GetFileName(directoryName) != "Mods")
        {
            text = directoryName;
            directoryName = Path.GetDirectoryName(text);
        }
        if (string.IsNullOrEmpty(directoryName))
        {
            _myModDirPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            _name = Path.GetFileName(_myModDirPath) ?? "";
        }
        else
        {
            _myModDirPath = text;
            _name = Path.GetFileName(text);
        }
    }

    public static void VersionConfirmation()
    {
        GameEvents.Twice_Second.RegWithEvent(SteamUGCRequest);
        string path = Path.Combine(MyModFolderPath, "plugin.json");
        if (File.Exists(path))
        {
            JObject jObject = JObject.Parse(File.ReadAllText(path));
            JToken jToken = jObject["version"];
            JToken jToken2 = jObject["workshop_id"];
            if (jToken != null)
            {
                _modVersion = SystemVersion.Parse(jToken.ToString());
            }
            if (jToken2 != null)
            {
                _workshopID = ulong.Parse(jToken2.ToString());
            }
        }
        ModProblemOverwrite($"{MyModName}  v{_modVersion}  Active!", MyModFolderPath, string.Empty, initIsError: false);
    }

    private static void ModProblemOverwrite(string initModName, string initModPath, string initDescription, bool initIsError)
    {
        ModProblems.AllModProblems.Remove(initModPath);
        ModProblems.AddModProblem(initModName, initModPath, initDescription, initIsError);
        foreach (IGui_GuiSystem activeGui in GuiDisplayer.GetSingleton().ActiveGuis)
        {
            activeGui.OnActivateGui();
        }
    }

    private static void SteamUGCRequest(ITimeStep t)
    {
        //IL_001c: Unknown result type (might be due to invalid IL or missing references)
        //IL_0022: Unknown result type (might be due to invalid IL or missing references)
        //IL_0027: Unknown result type (might be due to invalid IL or missing references)
        //IL_0043: Unknown result type (might be due to invalid IL or missing references)
        if (_workshopID != 0L && ++_requestCount <= 5)
        {
            SteamAPICall_t val = SteamUGC.RequestUGCDetails(
                new PublishedFileId_t(_workshopID),
                0u);

            _steamCall = new CallResult<SteamUGCRequestUGCDetailsResult_t>(
                (CallResult<SteamUGCRequestUGCDetailsResult_t>.APIDispatchDelegate)Callback);

            _steamCall.Set(
                val,
                (CallResult<SteamUGCRequestUGCDetailsResult_t>.APIDispatchDelegate)null);
        }
        else
        {
            GameEvents.Twice_Second.UnregWithEvent(SteamUGCRequest);
        }
    }

    private static void Callback(
        SteamUGCRequestUGCDetailsResult_t param,
        bool bIOFailure)
    {
        GameEvents.Twice_Second.UnregWithEvent(SteamUGCRequest);
        string rgchDescription = param.m_details.m_rgchDescription;
        if (string.IsNullOrEmpty(rgchDescription))
        {
            return;
        }
        using StringReader stringReader = new StringReader(rgchDescription);
        SystemVersion version = null;
        string text;
        while ((text = stringReader.ReadLine()) != null)
        {
            if (text.StartsWith("Latest version "))
            {
                version = SystemVersion.Parse(
                    text.Substring("Latest version ".Length));
                break;
            }
        }
        if (version != null && _modVersion.CompareTo(version) < 0)
        {
            ModProblemOverwrite(MyModName, MyModFolderPath + "UpdateText", "New version released! v" + version, initIsError: false);
        }
    }
}
