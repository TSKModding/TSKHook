namespace TSKHook;

public static class TranslationProtection
{
    // BattleEnd uses cost names as control identifiers, including the next-stage button.
    // The bare challenge-count identifier must also survive context-free text fallback.
    public static bool IsProtectedSource(string source) =>
        string.Equals(source, "挑戦回数", StringComparison.Ordinal);

    public static bool IsProtectedSourceHash(string sourceHash) =>
        string.Equals(sourceHash, "88e34b951e038683aefe3a36a87c6fac3d406e87e8550aa73513399fde8765ff", StringComparison.Ordinal);

    public static bool IsProtectedPath(string apiName, string path) =>
        string.Equals(apiName, "BattleEnd", StringComparison.Ordinal) &&
        (path.EndsWith(".retry_data.cost_name", StringComparison.Ordinal) ||
         path.EndsWith(".next_data.cost_name", StringComparison.Ordinal));
}
