using System;
using System.Collections.Generic;
using System.Text;

namespace TSKHook;

public static class UiTextKeyBuilder
{
    private static readonly string[] GenericIdentityFields =
    {
        "campaign_id", "category_id", "character_id", "event_id", "group_id", "id", "quest_id",
        "reward_id", "reward_type", "shop_id", "sister_unit_id", "stage_id", "status_effect_id", "unit_id"
    };

    public static UiTextKeyDescriptor Build(string apiName, string path, string source, string sourceHash,
        IReadOnlyDictionary<string, string> context)
    {
        context ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptor = BuildBase(apiName, path, source, sourceHash, context);
        if (!UsesSourceVariantIdentity(apiName, path, context)) return descriptor;
        var identity = new SortedDictionary<string, string>(descriptor.Identity, StringComparer.Ordinal) { ["source_hash"] = sourceHash };
        // Append without reconstructing scope: retain the exact legacy semantic identity.
        return new UiTextKeyDescriptor(descriptor.Key + ":source_hash=" + sourceHash, descriptor.Category, descriptor.Field, identity);
    }

    private static bool UsesSourceVariantIdentity(string apiName, string path, IReadOnlyDictionary<string, string> context)
    {
        var field = path[(path.LastIndexOf('.') + 1)..];
        return (context.ContainsKey("unit_id") && !path.Contains("enemy_list", StringComparison.OrdinalIgnoreCase) &&
                field is "character_name" or "unit_name" or "character_name_kana" or "full_name") ||
            (path.Contains("sister_unit_list", StringComparison.OrdinalIgnoreCase) && path.Contains("skill", StringComparison.OrdinalIgnoreCase) && field == "skill_detail") ||
            (apiName == "BossRushEventTop" && path.Contains(".stage_data", StringComparison.OrdinalIgnoreCase) && field == "tab_name") ||
            (apiName == "MainStageDetail" && field == "stage_name");
    }

    public static bool TryUpgradeCapturedKey(string apiName, string path, string source, string sourceHash,
        IReadOnlyDictionary<string, string> context, string oldKey, out UiTextKeyDescriptor descriptor)
    {
        if (TryUpgradeEnemyDataKey(apiName, path, source, sourceHash, context, oldKey, out descriptor)) return true;
        if (!UsesSourceVariantIdentity(apiName, path, context)) return false;
        var legacy = BuildBase(apiName, path, source, sourceHash, context).Key;
        if (oldKey != legacy && oldKey != legacy + "#" + sourceHash[..Math.Min(8, sourceHash.Length)]) return false;
        descriptor = Build(apiName, path, source, sourceHash, context);
        return descriptor.Key != oldKey;
    }

    private static UiTextKeyDescriptor BuildBase(string apiName, string path, string source, string sourceHash,
        IReadOnlyDictionary<string, string> context)
    {
        context ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var field = path[(path.LastIndexOf('.') + 1)..].Replace("[]", "", StringComparison.Ordinal);

        if (field is "limit_date_text" or "limit_time")
        {
            var category = path.Contains("mission", StringComparison.OrdinalIgnoreCase)
                ? "mission"
                : path.Contains("product", StringComparison.OrdinalIgnoreCase) ||
                  context.ContainsKey("product_id")
                    ? "shop"
                    : "misc";
            return CreateWithSourceHash(category, field, sourceHash, context,
                ("mission_type", false), ("mission_group_id", false), ("mission_id", false),
                ("shop_id", false), ("product_id", false));
        }

        if ((path.Contains("campaign_list", StringComparison.OrdinalIgnoreCase) || field == "dialogue") &&
            !context.ContainsKey("id"))
        {
            return CreateWithSourceHash("misc", field, sourceHash, context,
                ("event_id", false), ("shop_id", false));
        }

        if (path.Contains("enemy_list", StringComparison.OrdinalIgnoreCase) &&
            field is "character_name" or "unit_name" or "unit_detail")
        {
            return CreateWithSourceHash("misc", field, sourceHash, context, ("stage_id", false));
        }

        if (UsesEnemyDataSourceIdentity(path, context))
        {
            var enemyIdentity = GenericIdentity(context, sourceHash);
            enemyIdentity["source_hash"] = sourceHash;
            return FromIdentity("misc", field, enemyIdentity, apiName);
        }

        if (path.Contains("tab_stage_data", StringComparison.OrdinalIgnoreCase) && field == "tab_name")
        {
            return CreateWithSourceHash("misc", field, sourceHash, context,
                ("event_id", false), ("quest_id", false), ("shop_id", false));
        }

        if (path.Contains("voice_list", StringComparison.OrdinalIgnoreCase) &&
            context.ContainsKey("unit_id") && context.ContainsKey("voice_id") &&
            field is "voice_name" or "unlock_condition")
        {
            return Create("unit", field, context, ("unit_id", true), ("voice_id", true));
        }

        if ((path.Contains("voice_list", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("direction_list", StringComparison.OrdinalIgnoreCase)) &&
            context.ContainsKey("unit_id") && field == "unlock_condition")
        {
            return CreateWithSourceHash("unit", field, sourceHash, context, ("unit_id", true));
        }

        if (path.Contains("trade_list", StringComparison.OrdinalIgnoreCase) &&
            context.ContainsKey("trade_id") && field is "button_name" or "button_detail")
        {
            return Create("shop", field, context, ("gacha_id", false), ("trade_id", true));
        }

        if (path.Contains("stage_rank_condition_list", StringComparison.OrdinalIgnoreCase) &&
            context.ContainsKey("stage_id") && context.ContainsKey("condition_type"))
        {
            return CreateWithSourceHash("mission", field, sourceHash, context,
                ("stage_id", true), ("condition_type", true));
        }

        if (field.StartsWith("exclusive_unit_", StringComparison.OrdinalIgnoreCase) &&
            context.ContainsKey("exclusive_unit_id"))
        {
            return Create("unit", field, context, ("exclusive_unit_id", true));
        }

        if (field is "unit_name" or "character_name" or "character_name_kana" or "full_name" &&
            context.ContainsKey("unit_id"))
        {
            return Create("unit", field, context, ("unit_id", true), ("character_id", false),
                ("unit_illust_id", false), ("unit_view_type", false));
        }

        if (context.TryGetValue("item_id", out var itemId) &&
            (field.Contains("item", StringComparison.OrdinalIgnoreCase) || field == "detail"))
        {
            return itemId == "0"
                ? Create("item", field, context, ("item_type", false), ("item_id", true), ("cost_type", true))
                : Create("item", field, context, ("item_type", false), ("item_id", true));
        }

        if (context.ContainsKey("reward_id") &&
            (field.Contains("reward", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("reward_list", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("product_detail_list", StringComparison.OrdinalIgnoreCase)))
        {
            return CreateReward(field, context, source);
        }

        if (path.Contains("sister_unit_list", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            return CreateSisterSkill(field, path, context);
        }

        if (context.ContainsKey("equip_id") &&
            (field.Contains("equip", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("equip", StringComparison.OrdinalIgnoreCase)))
        {
            var includeSkill = context.ContainsKey("skill_id") &&
                               (path.Contains("skill", StringComparison.OrdinalIgnoreCase) ||
                                path.Contains("enchant", StringComparison.OrdinalIgnoreCase));
            return includeSkill
                ? Create("equip", field, context, ("equip_id", true), ("skill_id", true),
                    ("frame_no", false), ("effect_type", false), ("lv", false))
                : Create("equip", field, context, ("equip_id", true));
        }

        if (context.ContainsKey("ex_skill_id") &&
            path.Contains("skill_data_list", StringComparison.OrdinalIgnoreCase))
        {
            return CreateExSkill(field, path, context);
        }

        if (context.ContainsKey("skill_id") && path.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            return CreateSkill(field, context);
        }

        if (context.ContainsKey("mission_id") || path.Contains("mission", StringComparison.OrdinalIgnoreCase))
        {
            if (!context.ContainsKey("mission_type") && !context.ContainsKey("mission_group_id") &&
                !context.ContainsKey("mission_id") && !context.ContainsKey("id"))
            {
                return CreateWithSourceHash("mission", field, sourceHash, context);
            }

            return Create("mission", field, context, ("mission_type", false), ("mission_group_id", false),
                ("mission_id", false), ("id", false));
        }

        if (context.ContainsKey("product_id") || path.Contains("product", StringComparison.OrdinalIgnoreCase))
        {
            return Create("shop", field, context, ("shop_id", false), ("product_id", false), ("id", false));
        }

        if (context.ContainsKey("unit_id") &&
            (path.Contains("unit", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("character", StringComparison.OrdinalIgnoreCase)))
        {
            return Create("unit", field, context, ("unit_id", true), ("character_id", false));
        }

        return FromIdentity("misc", field, GenericIdentity(context, sourceHash), apiName);
    }

    public static bool UsesEnemyDataSourceIdentity(string path, IReadOnlyDictionary<string, string> context)
    {
        var field = path[(path.LastIndexOf('.') + 1)..];
        return path.Contains(".enemy_data.", StringComparison.OrdinalIgnoreCase) &&
            !context.ContainsKey("unit_id") && field is "character_name" or "unit_name" or "unit_detail";
    }

    public static bool TryUpgradeEnemyDataKey(string apiName, string path, string source, string sourceHash,
        IReadOnlyDictionary<string, string> context, string oldKey, out UiTextKeyDescriptor descriptor)
    {
        descriptor = null;
        if (!UsesEnemyDataSourceIdentity(path, context)) return false;
        var field = path[(path.LastIndexOf('.') + 1)..];
        var legacy = FromIdentity("misc", field, GenericIdentity(context, sourceHash), apiName).Key;
        if (oldKey != legacy && oldKey != legacy + "#" + sourceHash[..Math.Min(8, sourceHash.Length)]) return false;
        descriptor = Build(apiName, path, source, sourceHash, context);
        return descriptor.Key != oldKey;
    }

    private static SortedDictionary<string, string> GenericIdentity(IReadOnlyDictionary<string, string> context, string sourceHash)
    {
        var identity = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in GenericIdentityFields)
        {
            if (context.TryGetValue(name, out var value))
            {
                identity[name] = value;
            }
        }

        if (identity.Count == 0)
        {
            identity["source_hash"] = sourceHash[..Math.Min(12, sourceHash.Length)];
        }

        return identity;
    }

    private static UiTextKeyDescriptor CreateExSkill(string field, string path,
        IReadOnlyDictionary<string, string> context)
    {
        var identity = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["skill_id"] = context["ex_skill_id"]
        };
        if (context.TryGetValue("ex_skill_lv", out var level))
        {
            identity["lv"] = level;
        }

        if (field == "detail" &&
            !path.Contains("ex_skill_lv_list", StringComparison.OrdinalIgnoreCase))
        {
            identity["detail_variant"] = "summary";
        }

        var normalizedField = field switch
        {
            "ex_skill_name" => "skill_name",
            "detail" => "skill_detail",
            _ => field
        };
        return FromIdentity("skill", normalizedField, identity, null);
    }

    private static UiTextKeyDescriptor CreateSkill(string field,
        IReadOnlyDictionary<string, string> context)
    {
        var identity = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "skill_data_type", "lv", "unit_id" })
        {
            if (context.TryGetValue(name, out var value))
            {
                identity[name] = value;
            }
        }

        var skillId = context.TryGetValue("skill_id", out var capturedSkillId)
            ? capturedSkillId
            : null;
        if ((string.IsNullOrEmpty(skillId) || skillId == "-1") &&
            context.TryGetValue("m_skill_id", out var masterSkillId) &&
            !string.IsNullOrEmpty(masterSkillId) && masterSkillId != "-1")
        {
            identity["m_skill_id"] = masterSkillId;
        }
        else
        {
            identity["skill_id"] = string.IsNullOrEmpty(skillId) ? "missing" : skillId;
        }

        return FromIdentity("skill", field, identity, null);
    }

    private static UiTextKeyDescriptor CreateSisterSkill(string field, string path,
        IReadOnlyDictionary<string, string> context)
    {
        var identity = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[]
                 {
                     "sister_unit_id", "active_skill_id", "support_skill_id", "status_effect_id", "skill_type", "lv"
                 })
        {
            if (context.TryGetValue(name, out var value))
            {
                identity[name] = value;
            }
        }

        identity["skill_kind"] = path.Contains("extra_support_skill_data", StringComparison.OrdinalIgnoreCase)
            ? "extra_support"
            : path.Contains("active_skill_data", StringComparison.OrdinalIgnoreCase)
                ? "active"
                : "support";
        return FromIdentity("skill", field, identity, null);
    }

    private static UiTextKeyDescriptor CreateReward(string field, IReadOnlyDictionary<string, string> context,
        string source)
    {
        var identity = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "reward_type", "reward_id" })
        {
            if (context.TryGetValue(name, out var value))
            {
                identity[name] = value;
            }
        }

        if (!identity.ContainsKey("reward_id"))
        {
            identity["reward_id"] = "missing";
        }

        if (field == "reward_name")
        {
            identity["layout"] = source.Contains('\n') ? "multiline" : "singleline";
        }

        return FromIdentity("reward", field, identity, null);
    }

    private static UiTextKeyDescriptor Create(string category, string field,
        IReadOnlyDictionary<string, string> context, params (string Name, bool Required)[] fields)
    {
        var identity = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var fieldSpec in fields)
        {
            if (context.TryGetValue(fieldSpec.Name, out var value))
            {
                identity[fieldSpec.Name] = value;
            }
            else if (fieldSpec.Required)
            {
                identity[fieldSpec.Name] = "missing";
            }
        }

        return FromIdentity(category, field, identity, null);
    }

    private static UiTextKeyDescriptor CreateWithSourceHash(string category, string field, string sourceHash,
        IReadOnlyDictionary<string, string> context, params (string Name, bool Required)[] fields)
    {
        var descriptor = Create(category, field, context, fields);
        var identity = new SortedDictionary<string, string>(descriptor.Identity, StringComparer.Ordinal)
        {
            ["source_hash"] = sourceHash[..Math.Min(12, sourceHash.Length)]
        };
        return FromIdentity(category, field, identity, null);
    }

    private static UiTextKeyDescriptor FromIdentity(string category, string field,
        SortedDictionary<string, string> identity, string scope)
    {
        var key = new StringBuilder(category).Append(':');
        if (!string.IsNullOrEmpty(scope))
        {
            key.Append(Escape(scope)).Append(':');
        }
        foreach (var pair in identity)
        {
            key.Append(pair.Key).Append('=').Append(Escape(pair.Value)).Append(':');
        }
        key.Append(field);
        return new UiTextKeyDescriptor(key.ToString(), category, field, identity);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
}

public sealed record UiTextKeyDescriptor(string Key, string Category, string Field,
    SortedDictionary<string, string> Identity);
