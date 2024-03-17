using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TSKHook;

public class Translation
{
    public static Dictionary<string, string> nameDicts = new();
    public static Dictionary<string, Dictionary<string, string>> chapterDicts = new();

    public static void Init()
    {
        if (!TSKConfig.TranslationEnabled) return;

        var response =
            HttpRequester(
                "https://translation.lolida.best/download/tsk/tsk_name/zh_Hant/?format=json");
        if (response.IsSuccessStatusCode)
        {
            var responseContent = response.Content;
            var body = responseContent.ReadAsStringAsync().GetAwaiter().GetResult();

            nameDicts = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
            Plugin.Global.Log.LogInfo("[Translator] Character name translation loaded. Total: " + nameDicts.Count);
        }
        else
        {
            Plugin.Global.Log.LogWarning(
                "[Translator] Character name translation failed to load, character name wouldn't translate.");
        }

        var response2 =
            HttpRequester(
                "https://translation.lolida.best/download/tsk/tsk_subname/zh_Hant/?format=json");
        if (response2.IsSuccessStatusCode)
        {
            var responseContent2 = response2.Content;
            var body2 = responseContent2.ReadAsStringAsync().GetAwaiter().GetResult();

            var subNameDicts = JsonSerializer.Deserialize<Dictionary<string, string>>(body2);
            subNameDicts.ToList().ForEach(x => nameDicts.Add(x.Key, x.Value));
            Plugin.Global.Log.LogInfo("[Translator] Rando name translation loaded. Total: " + subNameDicts.Count);
        }
        else
        {
            Plugin.Global.Log.LogWarning(
                "[Translator] Rando name translation failed to load, rando name wouldn't translate.");
        }
    }

    public static void FetchChapterTranslation(string label)
    {
        var response = HttpRequester("https://translation.lolida.best/download/tsk/" + label + "/zh_Hant/?format=json");
        if (response.IsSuccessStatusCode)
        {
            var responseContent = response.Content;
            var body = responseContent.ReadAsStringAsync().GetAwaiter().GetResult();

            chapterDicts[label] = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
            Plugin.Global.Log.LogInfo("[Translator] Chapter translation loaded. Total: " + chapterDicts[label].Count);
        }
        else
        {
            chapterDicts[label] = new Dictionary<string, string>();
            Plugin.Global.Log.LogWarning(
                "[Translator] Chapter translation failed to load, chapter text wouldn't translate.");
        }
    }

    public static HttpResponseMessage HttpRequester(string url)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(MyPluginInfo.PLUGIN_NAME + "/" + MyPluginInfo.PLUGIN_VERSION);
        var task = Task.Run(() => client.GetAsync(url));
        task.Wait();

        return task.Result;
    }
}