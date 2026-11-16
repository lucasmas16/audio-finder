using System;
using System.Globalization;

namespace AudioFinder
{
    // Stored in settings.xml. Auto follows the Windows display language (Portuguese when it is pt-*, English otherwise).
    public enum Language { Auto, Portuguese, English }

    // Two languages only, so each call site carries both texts side by side: L.T("Salvar", "Save").
    internal static class L
    {
        private static bool english = SystemIsEnglish();
        internal static bool English { get { return english; } }
        internal static void Apply(Language preference) { english = preference == Language.English || (preference != Language.Portuguese && SystemIsEnglish()); }
        internal static string T(string portuguese, string englishText) { return english ? englishText : portuguese; }
        internal static string F(string portuguese, string englishText, params object[] args) { return String.Format(CultureInfo.CurrentCulture, T(portuguese, englishText), args); }
        private static bool SystemIsEnglish() { return !String.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "pt", StringComparison.OrdinalIgnoreCase); }
    }
}
