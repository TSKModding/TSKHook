namespace TSKHook;

public static class UiRenderedTextTranslator
{
    public static bool TryTranslate(string source, UiTranslationIndex translations, out string translation)
    {
        translation = null;
        if (translations == null || translations.ExactCount == 0 ||
            !CaptureTextFilter.ShouldCapture("$.rendered_text", source))
        {
            return false;
        }

        return translations.TryGetRenderedText(source, out translation) &&
            !string.Equals(source, translation, System.StringComparison.Ordinal);
    }
}
